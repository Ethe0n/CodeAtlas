# CodeAtlas
vb net code analyzer

<img width="960" height="673" alt="Image" src="https://github.com/user-attachments/assets/d3b398d0-e045-4d80-828b-3a25ee32c4c8" />

<img width="960" height="673" alt="Image" src="https://github.com/user-attachments/assets/5380992d-3840-4732-8b7b-25887ff274ad" />

# TODO
- [ ] UI 미리보기 창
- [ ] method flow 이미지로 내보내기
- [ ] method flow 쉬운 편집을 위해 draw.io로 내보내기
- [ ] Class overview 개선
- [ ] Project overview 개선


# Issue
- [x] On Error Resume Next와 같은 키워드가 블록으로 처리됨
- [x] ":"를 사용해서 문장이 연결된 경우 하나의 블록으로 처리됨
- [x] 가독성 향상을 위해 True / False Branch 색 통일
- [x] block N 과 같은 필요없는 블록이 출력됨
- [x] switch ~ case 문을 제대로 파싱 못함
- [ ] 클래스 구조에서 {클래스 변수명}.{변수 명}이 있을 때 클래스 변수 명은 생략되고 변수 이름만 나와서 제대로 된 정보 전달 안됨(Ex. test.value = 10 으로 나와야 하는데 value = 10으로 나옴)
- [ ] switch case의 case 분기 이후 if가 들어가면 flow가 꼬이는 문제
