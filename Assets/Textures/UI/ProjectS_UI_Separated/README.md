# Project S — 프레임/그라데이션 분리 팩 (원본 1:1)

기존 두 팩(스킬창 팩 · UI 팩 2)의 스프라이트 중 **내부 배경이 있는 39종**을
원본과 동일한 지오메트리·색·글로우 그대로 두 레이어로 분리했습니다.

- `{이름}_fill.png` — 그라데이션 배경만 (스캔라인·그리드 등 표면 텍스처 포함, 테두리 없음)
- `{이름}_outline.png` — 외곽선 + 코너 액센트 + 글리프(X, ?, 체크 등)만, 안쪽 완전 투명

같은 이름의 두 파일은 **캔버스 크기·정렬이 완전히 동일**합니다. RectTransform을 같은 값으로 두 Image를
겹치면(fill 아래 → outline 위) 원본과 픽셀 단위로 같은 결과가 나옵니다.

## 사용 조합
1. **둘 다**: fill 아래 + outline 위 = 원본과 동일
2. **프레임만**: outline만 배치 (뒤 배경/이펙트 노출)
3. **그라데이션만**: fill만 배치 (테두리 없는 패널)
4. **커스텀 배경**: 원하는 색/이미지 위에 outline만 겹치기

9-Slice Border 값·임포트 설정은 기존 팩 README와 동일하게 적용하면 됩니다 (outline·fill 모두 같은 Border 사용 가능).

## 분리된 페어 목록
| 파일 | 크기(px) | 비고 |
|---|---|---|
| `Pack1_SkillUI/01_frames/main_window_frame` | 2750×1600 | 메인 스킬창 |
| `Pack1_SkillUI/01_frames/passive_card_frame` | 840×1350 | 패시브 카드 |
| `Pack1_SkillUI/01_frames/skill_card_frame` | 480×605 | 전투 스킬 카드(배지 포함) |
| `Pack1_SkillUI/01_frames/number_badge` | 125×100 | 번호 태그 |
| `Pack1_SkillUI/01_frames/icon_slot_frame` | 340×340 | 아이콘 슬롯 |
| `Pack1_SkillUI/01_frames/enhance_frame` | 1200×400 | 강화 선택 패널 |
| `Pack1_SkillUI/01_frames/enhance_tab` | 480×125 | 강화 선택 탭 |
| `Pack1_SkillUI/01_frames/option_slot_frame` | 560×275 | 강화 옵션 슬롯 |
| `Pack1_SkillUI/01_frames/section_container` | 1775×1025 | 섹션 컨테이너 |
| `Pack1_SkillUI/02_buttons_badges/btn_square_empty` | 165×165 | 빈 정사각 버튼 |
| `Pack1_SkillUI/02_buttons_badges/btn_close` | 165×165 | 닫기 |
| `Pack1_SkillUI/02_buttons_badges/btn_help` | 165×165 | 도움말 |
| `Pack1_SkillUI/02_buttons_badges/btn_chip` | 400×140 | 범용 칩 |
| `Pack1_SkillUI/02_buttons_badges/status_chip_off` | 130×95 | 토글 상태 칩 |
| `Pack1_SkillUI/02_buttons_badges/status_chip_on` | 130×95 | 토글 상태 칩 |
| `Pack1_SkillUI/02_buttons_badges/hex_frame` | 164×180 | SP 육각 |
| `Pack1_SkillUI/02_buttons_badges/badge_enhance` | 250×125 | 강화 배지 |
| `Pack1_SkillUI/02_buttons_badges/badge_enhance_prongs` | 250×160 | 강화 배지 |
| `Pack1_SkillUI/02_buttons_badges/badge_or` | 125×125 | OR 배지 |
| `Pack1_SkillUI/03_indicators/dot_off` | 110×110 | 레벨 도트(빈 것) |
| `Pack1_SkillUI/03_indicators/gauge_track` | 675×70 | 게이지 트랙 |
| `Pack2_General/01_hud/hp_bar_frame` | 700×95 | HP 바 프레임 |
| `Pack2_General/01_hud/boss_bar_frame` | 1350×85 | 보스 HP 바 프레임 |
| `Pack2_General/01_hud/portrait_frame_hex` | 295×330 | 초상 육각 프레임 |
| `Pack2_General/01_hud/skill_slot_round` | 240×240 | 원형 스킬 슬롯 |
| `Pack2_General/01_hud/minimap_frame` | 420×420 | 미니맵 프레임 |
| `Pack2_General/02_menu/dialogue_frame` | 1670×420 | 대화창(스캔라인은 fill에 포함) |
| `Pack2_General/02_menu/name_plate` | 425×125 | 이름표 |
| `Pack2_General/02_menu/tooltip_frame` | 600×275 | 툴팁 |
| `Pack2_General/02_menu/list_item` | 840×140 | 리스트 행 |
| `Pack2_General/02_menu/list_item_selected` | 840×140 | 리스트 행(선택) |
| `Pack2_General/02_menu/tab_active` | 350×135 | 탭(활성) |
| `Pack2_General/02_menu/tab_inactive` | 350×135 | 탭(비활성) |
| `Pack2_General/02_menu/checkbox_off` | 105×105 | 체크박스(해제) |
| `Pack2_General/02_menu/checkbox_on` | 105×105 | 체크박스(선택) |
| `Pack2_General/02_menu/slider_handle` | 100×100 | 슬라이더 핸들 |
| `Pack2_General/02_menu/popup_frame` | 1020×620 | 팝업/모달 |
| `Pack2_General/02_menu/toast_banner` | 950×170 | 토스트 배너 |
| `Pack2_General/02_menu/keycap` | 125×125 | 키캡(하단 셰이딩은 fill에 포함) |

## 분리 불필요 — 원본 그대로 포함 (34개)
순수 라인(아이콘·브래킷·레티클 등) 또는 순수 필(게이지 필·쿨다운·글로우 등)이라 이미 단일 레이어입니다.
- `Pack1_SkillUI/03_indicators/dot_on.png`
- `Pack1_SkillUI/03_indicators/gauge_fill.png`
- `Pack1_SkillUI/02_buttons_badges/glyph_reset.png`
- `Pack1_SkillUI/04_accents/divider_diag.png`
- `Pack1_SkillUI/04_accents/divider_line.png`
- `Pack1_SkillUI/04_accents/header_tab_line.png`
- `Pack2_General/01_hud/boss_bar_fill.png`
- `Pack2_General/01_hud/cooldown_radial.png`
- `Pack2_General/01_hud/crosshair.png`
- `Pack2_General/01_hud/hp_bar_fill.png`
- `Pack2_General/01_hud/lockon_reticle.png`
- `Pack2_General/01_hud/sg_bar_fill.png`
- `Pack2_General/03_icons/icon_arrow.png`
- `Pack2_General/03_icons/icon_check.png`
- `Pack2_General/03_icons/icon_chip.png`
- `Pack2_General/03_icons/icon_gear.png`
- `Pack2_General/03_icons/icon_lock.png`
- `Pack2_General/03_icons/icon_map_pin.png`
- `Pack2_General/03_icons/icon_noise.png`
- `Pack2_General/03_icons/icon_quest.png`
- `Pack2_General/03_icons/icon_signal.png`
- `Pack2_General/03_icons/icon_star.png`
- `Pack2_General/03_icons/icon_warning.png`
- `Pack2_General/04_holo_deco/bracket_side.png`
- `Pack2_General/04_holo_deco/chevron_flow.png`
- `Pack2_General/04_holo_deco/circuit_trace.png`
- `Pack2_General/04_holo_deco/corner_bracket.png`
- `Pack2_General/04_holo_deco/glitch_bars.png`
- `Pack2_General/04_holo_deco/hazard_stripe_tile.png`
- `Pack2_General/04_holo_deco/hex_grid_tile.png`
- `Pack2_General/04_holo_deco/holo_base_glow.png`
- `Pack2_General/04_holo_deco/holo_ring.png`
- `Pack2_General/04_holo_deco/light_beam.png`
- `Pack2_General/04_holo_deco/scanline_tile.png`

## 참고
- 이 팩은 기존 두 팩의 **완전한 대체본**입니다(분리본 + 원본 통과분 = 전체 커버). 이전에 드린 간이 OutlineFill 팩은 형태가 단순화된 버전이니 이걸 기준으로 쓰세요.
- badge_enhance 계열은 발광이 몸체(fill)에서 나오므로 글로우를 fill 쪽에 뒀습니다. outline만 쓰면 얇은 주황 테두리가 됩니다.
