# Testing

## Running tests

```bash
dotnet test
```

---

## xUnit version pin — VirtualCanvas.Avalonia.Tests

`tests/VirtualCanvas.Avalonia.Tests` uses **xunit 2.6.2**, not the project-wide 2.9.0.

### Why

`Avalonia.Headless.XUnit 11.0.0` was compiled against `xunit.core 2.4.0`.
In xunit 2.9.0, `XunitTestAssemblyRunner.RunTestCollectionsAsync` calls
`SetupSyncContext` **only** when `ParallelAlgorithm == Aggressive`; in 2.4–2.6 it was
called unconditionally whenever parallelization was enabled.

`AvaloniaTestAssemblyRunner` overrides `SetupSyncContext` to initialize
`_session = HeadlessUnitTestSession.GetOrStartForAssembly(...)`.
When `SetupSyncContext` is skipped, `_session` remains `null`.
The first test case then crashes with:

```
System.NullReferenceException
   at Avalonia.Headless.XUnit.AvaloniaTestCaseRunner.RunTest(
      HeadlessUnitTestSession session, ...)   ← session is null
```

xunit 2.6.2 restores the unconditional call, fixing the crash.

### Retry condition

Upgrade `Avalonia.Headless.XUnit` (> 11.0.0) **or** confirm xunit 2.9+ support in a
newer Avalonia.Headless.XUnit release, then remove the pin and re-test.

### Where the fix lives

`tests/VirtualCanvas.Avalonia.Tests/VirtualCanvas.Avalonia.Tests.csproj` — xunit pin.
The two runtime bugs fixed alongside this are documented in the A-5.1 working-tree diff
(`git diff HEAD -- src/VirtualCanvas.Avalonia/Controls/`).
