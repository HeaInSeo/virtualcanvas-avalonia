# Decision Log

> 주요 기술 의사결정의 이유와 기대 효과를 기록한다.
> 약어: **VCA** = VirtualCanvas.Avalonia

---

## 의사결정 목록

### [DEC-001] VCRect: 프레임워크 독립 자체 구조체
- **날짜**: 2026-03 초
- **결정**: `System.Windows.Rect` / `Avalonia.Rect` 대신 `VCRect`(자체 `struct`) 사용
- **대안**: Avalonia.Rect 직접 사용, WPF Rect 그대로 참조
- **이유**:
  - `VirtualCanvas.Core`는 UI 프레임워크 의존이 없어야 한다. Avalonia 참조 시 Core가 Avalonia를 전이 의존하게 되어 다른 프레임워크에서 재사용 불가
  - WPF `System.Windows.Rect`는 Windows-only
  - `VCRect`는 `struct`이므로 힙 할당 없음 — QuadTree 내부 연산에서 GC 압력 없음
- **시맨틱 보존**:
  - `Empty = (+∞, +∞, −∞, −∞)` — WPF Empty 동일
  - `Infinite = (−∞, −∞, +∞, +∞)` — ISpatialIndex Extent 센티널
- **기대 효과**: Core 라이브러리가 Avalonia/WPF 비의존으로 어느 .NET 프레임워크에서도 참조 가능

---

### [DEC-002] Priority 순서: 높은 값이 먼저
- **날짜**: 2026-03 초
- **결정**: `PriorityQuadTree<T>`에서 `Priority` 값이 **높을수록** 먼저 반환
- **대안**: 낮은 값이 먼저 (min-heap 스타일)
- **이유**:
  - WPF 원본 동작 보존: QuadNode 링크드 리스트에서 tail=최솟값, tail.Next→···→최댓값 순으로 순회
  - 기존 ISpatialIndex 주석이 역방향으로 기술되어 있었으나 구현이 고우선순위 우선 → 주석 정정, 구현 유지
- **주의**: `ISpatialIndex.GetItemsIntersecting` 문서에 "higher Priority returned first" 명시

---

### [DEC-003] IntersectsWith 시맨틱: 경계 접촉 = 교차
- **날짜**: 2026-03 초
- **결정**: `VCRect.IntersectsWith`에서 경계 접촉(≥) 및 빈 rect → `true`
- **대안**: 엄격한 내부 교차만 (>)
- **이유**:
  - WPF `Rect.IntersectsWith` 동작 보존 (`WpfHelper.Intersects`)
  - 경계상의 점/선 아이템이 뷰포트 가장자리에 있을 때 누락되면 팝인(pop-in) 아티팩트 발생
  - 빈 rect → true는 WPF 원본 호환 — `HasItemsInside` 한계와 별개

---

### [DEC-004] ISpatialItem.OnMeasure 제거
- **날짜**: 2026-03 초
- **결정**: WPF 원본의 `ISpatialItem.OnMeasure(UIElement)` 멤버를 포팅하지 않음
- **대안**: Avalonia `Control`을 파라미터로 받는 동등 메서드 추가
- **이유**:
  - `UIElement`는 WPF 전용 타입. `Control`을 받으면 Core가 Avalonia에 의존
  - 실제 WPF 구현에서도 거의 사용되지 않는 확장점 (DevApp 포함 어느 소비자도 구현하지 않음)
  - `IVisualFactory.Realize(ISpatialItem, bool)`이 실현(realization) 시점 콜백 역할을 대신함
- **기대 효과**: Core가 UI 프레임워크 비의존 상태 유지

---

### [DEC-005] AddVisualChild 대체: VisualChildren.Insert + LogicalChildren
- **날짜**: 2026-03 중
- **결정**: WPF `AddVisualChild`/`RemoveVisualChild` → Avalonia `VisualChildren.Insert(idx, v)` + `LogicalChildren.Insert(idx, v)`
- **이유**:
  - Avalonia에는 `AddVisualChild`가 없음. `Panel`과 달리 `Control`을 직접 상속하면 `VisualChildren`/`LogicalChildren`을 수동 관리해야 함
  - ZIndex 정렬 삽입(`FindInsertIndex`)이 필요하므로 `idx` 기반 Insert가 필수
- **ZIndex 제거 시 주의**:
  - `UpdateVisualChildZIndex` 후 `_sortedVisuals`와 `LogicalChildren` 순서가 달라짐
  - → `RemoveVisualChildInternal`은 반드시 레퍼런스 기반 `LogicalChildren.Remove(visual)` 사용 (인덱스 사용 불가)
  - **Bug 2(A-5.1)**가 바로 이 이유로 발생했음

---

### [DEC-006] DispatcherOperation.Abort() 대체: CancellationTokenSource
- **날짜**: 2026-03 중
- **결정**: WPF `DispatcherOperation.Abort()` → `CancellationTokenSource` + `Dispatcher.UIThread.Post`
- **이유**:
  - Avalonia `Dispatcher.UIThread.Post`는 반환 값이 없어 작업 취소가 불가능
  - `CancellationTokenSource`를 스로틀링 루프 조건으로 사용, 새 realization 요청 시 기존 CTS를 Cancel하고 새 CTS 생성
  - **Bug 1(A-5.1)**: `_realizeCts = null` 타이밍 오류 → 소유권을 `continueAction` 클로저로 이전하여 해결

---

### [DEC-007] IVisualFactory: ISpatialItem 직접 전달
- **날짜**: 2026-03 중
- **결정**: `IVisualFactory.Realize(ISpatialItem item, bool force)` — `DataItem` 없음
- **대안**: WPF 원본처럼 `DataTemplate` 기반 `TemplatedVisualFactory`
- **이유**:
  - WPF `DataTemplate` 바인딩은 Avalonia에서 직접 이식 불가 (MVVM 전제, AXAML 의존)
  - VCA 소비자(DevApp, DagEdit)는 `ISpatialItem`을 직접 알고 있으므로 DataItem 계층이 불필요
  - `DefaultVisualFactory`(no-op)로 테스트/헤드리스 환경에서 안전하게 동작
- **기대 효과**: 소비자가 Realize 시점에 완전한 제어권 보유 (스타일, 캐시, 재사용 등)

---

### [DEC-008] Consumer-driven selection: VCA는 상태만 소유
- **날짜**: 2026-03 후반
- **결정**: `VirtualCanvas`는 `SelectedItem` 상태와 `SelectionChanged` 이벤트만 소유. 스타일 적용은 소비자 책임.
- **대안**: VCA가 선택 스타일(BorderBrush 등) 직접 변경
- **이유**:
  - VCA는 렌더링/가상화 인프라. UI 스타일 정책은 소비자(DevApp, DagEdit) 도메인
  - VCA가 스타일을 적용하면 소비자의 스타일과 충돌 가능
  - 가상화 후 재실현 시 스타일 소실 문제 → `RealizationCompleted` 이벤트로 소비자가 재적용
  - DagEdit는 선택 표현(Border 색상, 아이콘, Overlay 등)을 자체적으로 정의할 것이므로 VCA의 스타일 강제가 방해가 됨
- **소비자 계약**:
  ```csharp
  canvas.SelectionChanged += (_, e) => { Unstyle(e.OldItem); Style(e.NewItem); };
  canvas.RealizationCompleted += (_, _) => { if (canvas.SelectedItem != null) Style(canvas.SelectedItem); };
  ```
- **기대 효과**: VCA API가 DagEdit, DevApp 등 다양한 소비자 요구에 중립적으로 대응

---

### [DEC-009] GenerateDocumentationFile: 양 라이브러리 프로젝트
- **날짜**: 2026-03-07
- **결정**: `VirtualCanvas.Core.csproj`, `VirtualCanvas.Avalonia.csproj` 양쪽에 `<GenerateDocumentationFile>true</GenerateDocumentationFile>` 추가
- **대안**: 문서 없이 배포 (경고만 확인)
- **이유**:
  - NuGet 패키지 소비자(DagEdit 등)가 IDE에서 `///` XML 주석 IntelliSense를 얻으려면 `.xml`이 `lib/net8.0/` 안에 포함되어야 함
  - `NoWarn:1591` 추가로 문서화되지 않은 public 멤버에 대한 빌드 경고 억제 (향후 순차적으로 주석 보강 예정)
- **기대 효과**: NuGet 소비자의 IDE에서 API 힌트 자동 표시

---

### [DEC-010] NuGet publish 자동화 보류
- **날짜**: 2026-03-07
- **결정**: CI에 `dotnet pack` 검증 step만 추가. `dotnet nuget push`는 이번 diff에서 제외.
- **이유**:
  - 현재 버전 `0.1.0-dev` — 아직 API 안정 선언 전
  - DagEdit 통합 분석 결과에 따라 API가 변경될 수 있음 (특히 selection/interaction 계약)
  - publish 자동화는 API 안정화 후 태그 기반 트리거(`on: push: tags: ['v*']`)로 추가 예정
- **기대 효과**: 패키지 품질(구조, README, XML) 검증은 매 CI마다 실행되고, 실수 publish는 방지

---

### [DEC-011] xunit 버전 분리: Core 2.9.0 / Avalonia 2.6.2
- **날짜**: 2026-03 중
- **결정**: `VirtualCanvas.Core.Tests`는 xunit 2.9.0, `VirtualCanvas.Avalonia.Tests`는 xunit **2.6.2** 고정
- **이유**:
  - xunit 2.9.0에서 `XunitTestAssemblyRunner.SetupSyncContext`가 `ParallelAlgorithm.Aggressive`일 때만 호출됨
  - `Avalonia.Headless.XUnit 11.0.0`은 `SetupSyncContext` 진입을 가정 → `_session = null`로 크래시
  - 2.6.2에서는 항상 호출됨 — 구조적 호환
- **해소 조건**: `Avalonia.Headless.XUnit` 11.x 이후 버전이 xunit 2.9.x와의 호환성을 공식 보장할 때
- **참조**: `docs/TESTING.md` 전문

---

### [DEC-012] SpatialSelectionChangedEventArgs 네이밍 (충돌 회피)
- **날짜**: 2026-03 후반
- **결정**: `SelectionChangedEventArgs` 대신 `SpatialSelectionChangedEventArgs`로 명명
- **이유**:
  - `Avalonia.Controls.SelectionChangedEventArgs`가 이미 존재하며 `using Avalonia.Controls` 환경에서 이름 충돌 발생
  - 소비자 코드에서 명시적 fully-qualified 이름을 강제하지 않으려면 이름이 달라야 함
  - `Spatial` 접두어가 도메인을 명확히 전달

---

### [DEC-014] Viewport contract test 범위: origin only (Width/Height 제외)
- **날짜**: 2026-03-07
- **결정**: `ViewportContractTests`에서 `ActualViewbox.X/Y`(origin)만 테스트. Width/Height는 테스트하지 않음.
- **이유**:
  - `ActualViewbox.Width = Bounds.Width / Scale`에서 `Bounds`는 Avalonia 레이아웃 패스 이후에만 유효
  - 헤드리스 테스트에서 `VirtualCanvas`를 Window에 호스팅하지 않으면 `Bounds = (0,0)` → Width/Height 항상 0
  - Origin(`X/Y`)은 `Offset`과 `Scale`만으로 계산되므로 레이아웃 없이도 정확히 검증 가능
  - DagEdit 통합 seam의 핵심은 "특정 화면 좌표가 어느 world 좌표에 해당하는가" → origin 공식이 그 seam
- **기대 효과**: 레이아웃 복잡도 없이 좌표 계약을 잠금. Width/Height는 `Bounds.Size`에서 직접 파생되는 trivial 계산이라 별도 회귀 위험 낮음.

---

### [DEC-015] Public API: stable contract vs advanced/provisional 이단계 분류
- **날짜**: 2026-03-07
- **결정**: README Public API 섹션을 "Stable contract"와 "Advanced / provisional"로 분리.
- **Stable contract**:
  - Properties: `Items`, `Scale`, `Offset`, `SelectedItem`, `IsVirtualizing`, `ActualViewbox`, `UseRenderTransform`, `VisualFactory`
  - Events: `SelectionChanged`, `RealizationCompleted`
  - Methods: `VisualFromItem`, `ItemFromVisual`
- **Advanced / provisional**: `ThrottlingLimit`, `IsPaused`, `ComputeOutlineGeometry`, `RealizeItem`, `ForceVirtualizeItem`, `RealizeItems`, `Clear`, `GetVisualChildren`, `BeginUpdate`, `EndUpdate`, `InvalidateReality`, `NotifyOnRealizationCompleted`, 모든 lifecycle events
- **이유**:
  - DagEdit 통합 시 소비자가 사용할 API와 내부 기계 장치 API를 README 수준에서 구분
  - "Stable" 목록은 DagEdit와의 통합 계약으로 간주, 변경 시 minor version 범프
  - "Advanced/provisional"은 미래 리팩토링(스로틀링 알고리즘 변경, 라이프사이클 훅 재설계 등)에서 변경될 수 있음
  - API surface를 줄이지 않고 문서만으로 기대치를 설정하는 점진적 접근

---

### [DEC-013] Multi-selection 보류: DagEdit 경계 분석 우선
- **날짜**: 2026-03-07
- **결정**: 다중 선택(`SelectedItems`, `SelectionMode`), 러버밴드 선택, 키보드 내비게이션을 VCA Phase D-1 이전으로 보류
- **이유**:
  - DagEdit는 자체 `DagEditorCanvas.cs` + `ViewportTransform.cs`를 보유. 상호작용(interaction) 레이어가 이미 구현됨
  - VCA가 interaction 정책을 내장하면 DagEdit의 기존 레이어와 책임이 겹칠 위험
  - VCA의 역할은 렌더링/가상화 **인프라**. selection 정책은 소비자 도메인
  - DagEdit 통합 보고서 작성 후 "VCA가 어떤 선택 primitive를 제공해야 하는가"를 통합 관점에서 결정
- **기대 효과**: 불필요한 API surface 추가 없이 계약을 잠금. DagEdit와의 통합이 명확해진 후 최소한의 변경으로 확장 가능
