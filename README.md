# CodeAtlas
CodeAtlas - VB(Visual Basic) .net code analyzer

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
- [ ] data.json 파일로 빼서 매번 로딩 반복 안하도록 하기

# Issue
- [x] On Error Resume Next와 같은 키워드가 블록으로 처리됨
- [x] ":"를 사용해서 문장이 연결된 경우 하나의 블록으로 처리됨
- [x] 가독성 향상을 위해 True / False Branch 색 통일
- [x] block N 과 같은 필요없는 블록이 출력됨
- [x] switch ~ case 문을 제대로 파싱 못함
- [x] 클래스 구조에서 {클래스 변수명}.{변수 명}이 있을 때 클래스 변수 명은 생략되고 변수 이름만 나와서 제대로 된 정보 전달 안됨(Ex. test.value = 10 으로 나와야 하는데 value = 10으로 나옴)
- [x] switch case의 case 분기 이후 if가 들어가면 branch가 꼬이는 문제
- [ ] UI preview 작동 제대로 안 함
- [ ] draw.io export 시 invalid data 오류 발생
- [ ] project overview, field table 등등 내용이 많으면 로딩 및 스크롤 시 렉 발생
- [ ] 가끔 flow chart에서 main graph와 연결 안된 sub graph가 있음.
