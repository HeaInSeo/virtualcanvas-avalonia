# Progress Log

> 실시간 작업 기록. 각 단계의 진행 상황, 문제, 해결 방안을 기록한다.
> 약어: **VCA** = VirtualCanvas.Avalonia (이 라이브러리 전체)

---

## 로드맵

| # | 단계 | 상태 | 목표 |
|---|------|------|------|
| A-1 | 솔루션 스켈레톤 | ✅ 완료 | 프로젝트 구조 + 빌드 확인 |
| A-2 | VCRect | ✅ 완료 | 프레임워크 독립 rect 구조체 |
| A-3 | PriorityQuadTree 포팅 | ✅ 완료 | WPF 원본 → Core 이식, 36 tests |
| A-4 | ISpatialIndex / ISpatialItem 계약 확정 | ✅ 완료 | 공개 API 계약 잠금 |
| A-5 | Avalonia VirtualCanvas Control | ✅ 완료 | 레이아웃/렌더링/가상화/스로틀링 |
| A-5.1 | 버그 수정 + 회귀 테스트 | ✅ 완료 | RealizationCompleted, ZIndex 버그 2건 |
| A-5.2 | 문서 + 멀티배치 회귀 테스트 | ✅ 완료 | TESTING.md, 41 tests |
| B | DevApp | ✅ 완료 | 5,000 아이템 팬/줌 데모 |
| B-2 | PerformanceTelemetry | ✅ 완료 | 프레임 타임, 이벤트율, GC 할당 |
| B-2.1 | Telemetry 오염 제거 | ✅ 완료 | RAF 종료 가드, string 생성 제거 |
| B-3 | Hit-test 단일 선택 | ✅ 완료 | ViewToWorld 역변환, ZIndex tiebreak |
| C-0 | Library boundary hardening | ✅ 완료 | SelectedItem StyledProperty, SelectionChanged |
| C-1 | 패키징 / 소비 가능성 고정 | ✅ 완료 | pack metadata, smoke tests 4개 |
| D-0 | Release hardening | ✅ 완료 | CHANGELOG, XML docs, CI pack 검증 |
| D-1 | DagEdit 통합 분석 | 🔲 대기 | VCA → DagEdit 렌더링 백엔드 적합성 평가 |
| D-2 | 다중 선택 | 🔲 보류 | DagEdit 경계 분석 후 판단 |
| E | 1.0.0 릴리스 | 🔲 예정 | API 안정화, NuGet publish |

---

## 단계별 기록

### [A-1] 솔루션 스켈레톤
- **날짜**: 2026-03 초
- **수행 내용**:
  - `VirtualCanvas.Avalonia.sln` 생성
  - 4개 프로젝트: Core, Avalonia, DevApp, Tests
  - TargetFramework: net8.0, Avalonia 11.0.0
- **검증 지표**: `dotnet build` 성공
- **다음 단계**: A-2 — VCRect

---

### [A-2] VCRect
- **날짜**: 2026-03 초
- **수행 내용**:
  - `src/VirtualCanvas.Core/Geometry/VCRect.cs` 신규
  - WPF `Rect` 시맨틱 완전 재현 (Empty, Infinite, IntersectsWith, Contains, Union)
  - `Empty = (+∞, +∞, −∞, −∞)`, `Infinite = (−∞, −∞, +∞, +∞)`
- **검증 지표**: 단위 테스트 통과
- **결정 참조**: DEC-001 (VCRect 자체 구조체)
- **다음 단계**: A-3 — PriorityQuadTree

---

### [A-3] PriorityQuadTree 포팅
- **날짜**: 2026-03 초
- **수행 내용**:
  - WPF 원본 `System.Windows.Rect` → `VCRect` 교체
  - `PriorityQuadTree<T>`, `PriorityQueue`, `QuadNode`, `Quadrant` 4파일
  - `IntersectsWith` 시맨틱: 경계 접촉(≥) = 교차, 빈 rect → true
  - Priority 방향: 높은 값이 먼저 반환
- **검증 지표**: 36 tests 통과
- **결정 참조**: DEC-002 (Priority 순서), DEC-003 (IntersectsWith 시맨틱)
- **다음 단계**: A-4 — 계약 확정

---

### [A-3.1] 계약·주석 정리
- **날짜**: 2026-03 초
- **수행 내용**:
  - `HasItemsInside` 한계 주석 명시 (child quadrant 포함 조건 기반 재귀)
  - Priority 방향 주석 정정 (ISpatialIndex 주석이 역방향이었음)
  - `GetItemsInside`: `GetIntersectingNodes + filter` 방식으로 올바르게 동작함 확인
- **검증 지표**: 기존 36 tests 유지

---

### [A-4] ISpatialIndex / ISpatialItem 계약 확정
- **날짜**: 2026-03 초
- **수행 내용**:
  - `ISpatialItem`: `Bounds`, `Priority`, `ZIndex`, `IsVisible`
  - `ISpatialIndex`: `GetItemsIntersecting`, `Changed`, `Extent`, `Any`
  - `SpatialIndex`: `PriorityQuadTree<ISpatialItem>` 어댑터
  - `ISpatialItem.OnMeasure(UIElement)` 제거 (WPF 의존)
- **결정 참조**: DEC-004 (OnMeasure 제거)
- **다음 단계**: A-5 — VirtualCanvas Control

---

### [A-5] Avalonia VirtualCanvas Control
- **날짜**: 2026-03 중
- **수행 내용**:
  - `VirtualCanvas.cs` + `VirtualCanvas.Throttling.cs`
  - `StyledProperty`: Items, Scale, Offset, IsVirtualizing, UseRenderTransform
  - `DirectProperty`: ActualViewbox (read-only)
  - `IVisualFactory` + `DefaultVisualFactory`
  - ZIndex 정렬: `_sortedVisuals` + `VisualChildren` 동기 유지
  - 스로틀링: `Dispatcher.UIThread.Post` 기반 자기조정 배치
  - `TransformGroup(ScaleTransform, TranslateTransform)` 기반 pan/zoom
- **검증 지표**: 기본 렌더링 동작 확인
- **결정 참조**: DEC-005 (AddVisualChild 대체), DEC-006 (DispatcherOperation 대체), DEC-007 (IVisualFactory)
- **다음 단계**: A-5.1 — 버그 수정

---

### [A-5.1] 버그 수정 + 회귀 테스트
- **날짜**: 2026-03 중
- **수행 내용**:
  - **Bug 1 (RealizationCompleted)**: `_realizeCts = null` 시점이 `ReferenceEquals` 가드 앞에 있어 완료 이벤트 미발생 → `_realizeCts = null` 제거, 소유권을 `continueAction`으로 이전
  - **Bug 2 (LogicalChildren)**: ZIndex 재정렬 후 인덱스 기반 `RemoveAt`가 잘못된 요소 제거 → 레퍼런스 기반 `LogicalChildren.Remove(visual)`로 교체
  - 회귀 테스트 2개 (`VirtualCanvas.Avalonia.Tests`)
- **검증 지표**: 36 + 2 = 38 tests 통과

---

### [A-5.2] 문서 + 멀티배치 회귀 테스트
- **날짜**: 2026-03 중
- **수행 내용**:
  - `docs/TESTING.md`: xunit 2.6.2 핀 이유, Avalonia.Headless 설명
  - `IVisualFactory` XML 주석 보강
  - 멀티배치 회귀 테스트 (ThrottlingLimit=10, 11개 아이템 → 2배치 경계)
  - `VirtualCanvas.Avalonia.SmokeTests` 프로젝트 신설 (xunit 2.6.2 pin 없음)
- **검증 지표**: 41 tests 통과
- **다음 단계**: CI 구축

---

### [CI A-5.2] GitHub Actions verify.yml
- **날짜**: 2026-03 중
- **수행 내용**:
  - `.github/workflows/verify.yml` 신규
  - push/PR → build + test + coverage (Cobertura)
  - `global.json` 추가 (.NET 8.0.x 고정)
  - TRX + coverage XML 아티팩트 업로드, Job Summary 생성
- **검증 지표**: CI 실행 성공

---

### [B] DevApp — 팬/줌/데모
- **날짜**: 2026-03 중
- **수행 내용**:
  - `DemoItem`, `DemoVisualFactory`: ISpatialItem 구현체 + 색깔 Border 팩토리
  - `SpatialIndex` 어댑터 확인 (`Extent` 설정 → `Insert` → `RaiseChanged()`)
  - `MainWindow.axaml`: VirtualCanvas + 마우스 팬(PointerPressed/Moved/Released) + 줌(PointerWheelChanged)
  - 팬: `Canvas.Offset` 직접 업데이트
  - 줌: 커서 기준 `worldUnderCursor * newScale − cursor` 공식
  - 상태바: Scale, Offset, Viewbox, Realized count
- **검증 지표**: 5,000 아이템 팬/줌 정상 동작

---

### [B-2] PerformanceTelemetry
- **날짜**: 2026-03 중
- **수행 내용**:
  - `PerformanceTelemetry.cs`: 프레임 타임, 팬/줌/realize 이벤트율, GC alloc (`GetAllocatedBytesForCurrentThread`)
  - `MainWindow`: RAF(RequestAnimationFrame) 루프 + 1초 DispatcherTimer 스냅샷
  - 2행 상태바: 뷰포트 상태 + 텔레메트리 행 분리
- **검증 지표**: 실시간 수치 표시 확인

---

### [B-2.1] Telemetry 오염 제거
- **날짜**: 2026-03 중
- **수행 내용**:
  - 이벤트 핸들러 내 string 생성 제거 (string formatting을 스냅샷 시점으로 이전)
  - RAF 종료 가드: `_rafEnabled = false` 체크로 Closed 후 RAF 연쇄 방지

---

### [B-3] Hit-test 단일 선택
- **날짜**: 2026-03 중
- **수행 내용**:
  - `ViewToWorld(viewPt)`: `(x + Offset.X) / Scale`
  - 1px 쿼리 rect로 `GetItemsIntersecting` → 픽셀 정확 히트
  - ZIndex tiebreak: 동일 위치에 겹칠 때 높은 ZIndex 우선
  - 클릭 판정: 5px 이내 displacement = 클릭 (드래그와 구분)
  - 토글: 동일 아이템 재클릭 → `SelectedItem = null`
- **검증 지표**: 선택/해제 정상 동작

---

### [C-0] Library Boundary Hardening
- **날짜**: 2026-03 후반
- **수행 내용**:
  - `SelectedItem`을 `StyledProperty<ISpatialItem?>`로 공개
  - `SelectionChanged` 이벤트 + `SpatialSelectionChangedEventArgs` 신규
  - 네이밍: `SpatialSelectionChangedEventArgs` (Avalonia.Controls.SelectionChangedEventArgs 충돌 회피)
  - VCA: 상태+이벤트 소유, 스타일 강제 없음
  - DevApp: `OnCanvasSelectionChanged`에서 Border 스타일 직접 제어
  - `RealizationCompleted` 이벤트 추가 (재실현 후 스타일 재적용 훅)
- **결정 참조**: DEC-008 (Consumer-driven selection)
- **검증 지표**: 선택 스타일 적용/복원 정상 동작

---

### [C-1] 패키징 / 소비 가능성 고정
- **날짜**: 2026-03 후반
- **수행 내용**:
  - 양 프로젝트 csproj에 NuGet pack metadata 추가 (`0.1.0-dev`)
  - `DefaultVisualFactory` `public`으로 가시성 변경
  - `VirtualCanvas.Avalonia.SmokeTests` 프로젝트: 내부 헬퍼 없이 공개 API만 사용하는 smoke test 4개
- **검증 지표**: 45 tests 통과 (36 Core + 5 Avalonia + 4 Smoke)
- **다음 단계**: D-0 — Release hardening

---

### [D-0] Release Hardening
- **날짜**: 2026-03-07
- **수행 내용**:
  - `GenerateDocumentationFile=true` + `NoWarn:1591` — 양 라이브러리 csproj
  - `VirtualCanvas.Core.csproj`: `PackageReadmeFile`, README.md `ItemGroup` 추가
  - CI `verify.yml`: `pack_core` / `pack_avalonia` step 추가 (`--no-build`), nupkg artifact 업로드, Summary Pack 섹션
  - `CHANGELOG.md` 신규 (0.1.0-dev 전체 내역)
- **검증 지표**:
  - 양 패키지: `.dll` + `.xml` + `README.md` 포함, 경고 0
  - `dotnet pack --no-build` CI 조건 통과
  - 45 tests 전원 통과
- **public API 영향**: 없음 (XML docs 추가는 API surface 변경 아님)
- **결정 참조**: DEC-009 (GenerateDocumentationFile), DEC-010 (publish 자동화 보류)

---

## 향후 과제

| 우선순위 | 내용 | 판단 조건 |
|---------|------|-----------|
| High | DagEdit 통합 분석 — VCA가 렌더링 백엔드로 적합한지 평가 | DagEdit `DagEditorCanvas.cs` 의존 분석 |
| High | Selection/Interaction 책임 경계 확정 | DagEdit 통합 분석 결과 기반 |
| Medium | 다중 선택 (`SelectedItems`, `SelectionMode`) | 경계 확정 후 착수 |
| Medium | 러버밴드 선택 | 다중 선택 이후 |
| Medium | 키보드 내비게이션 (방향키 팬, Escape 해제) | 경계 확정 후 착수 |
| Medium | xunit 2.6.2 pin 해소 (Avalonia.Headless.XUnit 업그레이드 추적) | Avalonia 11.x 패치 대응 |
| Low | StyleCop / `<Nullable>enable</Nullable>` 경고 0건 달성 | 코드 스타일 안정화 |
| Low | 1.0.0 릴리스 — NuGet publish CI step 추가 | API 안정 선언 후 |
