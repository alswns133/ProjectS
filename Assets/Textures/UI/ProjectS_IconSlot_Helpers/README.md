# 아이콘 슬롯 자연스럽게 넣기 — 보조 스프라이트 & 세팅

세 파일 모두 icon_slot_frame과 **같은 캔버스(340×340)·같은 정렬**입니다.
슬롯의 RectTransform 값을 그대로 복사해서 겹치면 자동으로 맞습니다.

## 하이어라키 (위에서 아래 = 렌더 순서 아래에서 위)
```
SkillSlot (예: 140×140 @FHD)
├─ 1_Fill        Image = icon_slot_frame_fill
├─ 2_IconMask    Image = icon_mask  + Mask 컴포넌트 (Show Mask Graphic: OFF)
│   └─ Icon      Image = 스킬 아이콘 (Anchor: Stretch, Offset 0 → 마스크가 라운드·여백 처리)
├─ 3_Shadow      Image = icon_inner_shadow  (Raycast Target: OFF)
├─ 4_Gloss(선택)  Image = icon_gloss         (Raycast Target: OFF)
└─ 5_Outline     Image = icon_slot_frame_outline
```

## 핵심 포인트
- **프레임(outline)이 항상 최상단** → 보더·코너 브래킷이 아이콘을 살짝 덮으면서 "끼워진" 느낌이 남.
- **icon_mask**: 보더 안쪽 라인에 맞춘 흰 라운드 사각. Mask의 스텐실 경계가 보더 밑에 숨어서 계단 현상도 안 보임.
- **icon_inner_shadow**: 가장자리를 살짝 어둡게 눌러서 아이콘이 슬롯 안으로 파묻힌 깊이감을 줌. 이게 자연스러움의 8할.
- **icon_gloss**: 상단 미세 하이라이트(선택). 과하면 촌스러우니 기본 알파 그대로 권장.

## 팁
- 아이콘 소스는 배치 크기의 2배 이상(140px 슬롯이면 280px+)으로 준비해야 흐릿하지 않음.
- 아이콘에 살짝 여백을 주고 싶으면 Icon의 Rect를 Stretch 대신 90~95% 크기로.
- Mask를 쓰기 싫으면(드로우콜 민감 시) 아이콘 PNG 제작 단계에서 코너 라운드(r≈22px @280px)를 직접 구워도 됨 — 그 경우 2_IconMask 없이 Icon만 두면 됨.
- 다른 슬롯(스킬 카드, 원형 슬롯 등)용 마스크·섀도가 필요하면 말해주면 같은 규격으로 뽑아줌.
