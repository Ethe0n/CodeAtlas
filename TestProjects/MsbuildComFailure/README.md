# MSBuild COM Failure

- 실제 CATIA 없이 COM 참조 실패를 재현하기 위한 솔루션
- `FakeCatiaTypes`는 정상 소스 분석을 위한 가짜 CATIA 타입 프로젝트
- `UnregisteredFakeCatiaTypeLib`는 의도적으로 등록되지 않은 COM 참조
- 향후 직접 로더에서는 `COMReference`를 건너뛰고 `ProjectReference`와 소스를 분석할 예정
- .NET Framework 4.5.2 Targeting Pack이 필요할 수 있음
