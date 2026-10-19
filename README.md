# CodeAtlas
CodeAtlas - VB(Visual Basic) .net code analyzer

Security: only open trusted solutions. MSBuild loading can execute build logic.
The console requires `--trust-projects`; see [security boundaries and checks](SECURITY.md).

<img width="960" height="673" alt="Image" src="https://github.com/user-attachments/assets/d3b398d0-e045-4d80-828b-3a25ee32c4c8" />

<img width="960" height="673" alt="Image" src="https://github.com/user-attachments/assets/5380992d-3840-4732-8b7b-25887ff274ad" />

# TODO
- [ ] UI 미리보기 창
- [x] method flow 이미지로 내보내기
- [x] method flow 쉬운 편집을 위해 draw.io로 내보내기
- [x] Class overview 개선
- [x] field, property 테이블로 정리
- [x] Project overview 개선
- [ ] 트리 레벨 0, 1, 2 조절하기
- [ ] UI Controls, UI Event handlers 좀 더 보기 편하게
- [x] 검색 기능 추가
- [ ] method flow chart 로컬 AI와 연동
- [ ] 코드 진단 정보 출력 

# Issue
- [x] On Error Resume Next와 같은 키워드가 블록으로 처리됨
- [x] ":"를 사용해서 문장이 연결된 경우 하나의 블록으로 처리됨
- [x] 가독성 향상을 위해 True / False Branch 색 통일
- [x] block N 과 같은 필요없는 블록이 출력됨
- [x] switch ~ case 문을 제대로 파싱 못함
- [x] 클래스 구조에서 {클래스 변수명}.{변수 명}이 있을 때 클래스 변수 명은 생략되고 변수 이름만 나와서 제대로 된 정보 전달 안됨(Ex. test.value = 10 으로 나와야 하는데 value = 10으로 나옴)
- [ ] switch case의 case 분기 이후 if가 들어가면 branch가 꼬이는 문제
  - [ ] switch case 문에서 case 분기 이후, if else end if로 구현했을 때 branch가 심하게 꼬이는 문제
- [ ] if else가 여러개 중첩되는 경우 branch 꼬임 발생
- [ ] UI preview 작동 제대로 안 함
- [ ] draw.io export 시 invalid data 오류 발생(웹 버전은 정상 작동)
- [x] project overview, field table 등등 내용이 많으면 로딩 및 스크롤 시 렉 발생
- [x] 가끔 flow chart에서 main graph와 연결 안된 sub graph가 있음.
  - [x] Try Catch
  - [x] Using 
- [x] 변수에 문자열 대입 시, 따옴표 생략되는 문제(Ex. [a = ""]이면 [a = ] 으로 표시됨)
- [x] OR나 AND로 길게 연결된 조건문이 있는 경우 한 줄로 길게 나열됨(특정 단위로 줄 나눔 필요)
- [x] 함수의 Relations에서 파라미터 리스트가 긴 경우 한 줄로 길게 나열됨(특정 단위로 줄 나눔 필요)
- [ ] 함수의 flow에서 파라미터가 긴 경우 한 줄로 길게 나열됨(특정 단위로 줄 나눔 필요)
- [x] 클래스 파일은 정상 분석되지만 모듈 파일은 존재하지 않음.
- [x] vbCrLf와 같은 특수 문자 UI 내에서는 변환 필요
- [x] 문자열 "&" 연산자가 "Concatenate"로 변환됨. 각 문자열 요소에 대해서 { }로 구분하고 &는 +로 변경 필요
- [x] Relations에 현재 노란색으로 표시되는 기본 함수들은 빼도 될 듯
