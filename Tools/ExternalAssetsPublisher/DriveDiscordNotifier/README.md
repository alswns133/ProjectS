# Drive 기준 Discord 배포 알림

이 알림기는 Publisher가 아니라 **Google Drive의 `manifest.json`**을 기준으로 동작합니다.

- Patch ZIP과 스냅샷은 게시 전 중간 단계이므로 알리지 않습니다.
- `manifest.json`의 `latestVersion`이 증가했을 때만 Discord에 알립니다.
- 따라서 Publisher 자동 게시·수동 게시·다른 배포 도구 모두 같은 알림 규칙을 공유합니다.

## 최초 설정 (관리자 1명)

1. [Google Apps Script](https://script.google.com/home)에서 **새 프로젝트**를 만듭니다.
2. `Code.gs` 내용을 이 폴더의 [`Code.gs`](Code.gs) 내용으로 전부 교체하고 저장합니다.
3. 코드 맨 위 `CONFIG`에서 **따옴표 안 두 곳만** 채웁니다.

   | 코드 항목 | 붙여넣을 값 |
   | --- | --- |
   | `manifestFileId` | 팀원 런처가 읽는 Drive `manifest.json`의 **파일 ID만** |
   | `discordWebhookUrl` | 새로 발급한 Discord Webhook URL 전체 |

   Webhook URL은 비밀값입니다. Git, manifest.json, 팀 채팅에 올리지 마세요.

4. 상단 실행 함수에서 `initializeNotifier`를 선택해 **한 번 실행**하고 권한을 승인합니다.
   - 현재 최신 버전을 기준선으로만 저장합니다. 기존 버전 알림은 보내지 않습니다.
5. `notifyCurrentRelease`를 한 번 실행해 Discord 시험 메시지가 오는지 확인합니다.
6. `installFiveMinuteTrigger`를 한 번 실행합니다.
   - 이후 5분마다 Drive manifest를 확인해 새 버전만 알립니다.

## 알림 내용

새 `latestVersion`이 감지되면 Discord에 다음을 보냅니다.

- 배포 버전(vN), 이전 버전
- ZIP 이름, Base/Patch 종류
- 삭제 파일 수(있을 때)
- 팀원에게 Launcher 업데이트 안내

## 운영 규칙

- 알림 기준은 **Drive의 manifest 게시 성공**입니다. ZIP만 올라가고 manifest가 갱신되지 않으면 알리지 않습니다.
- Discord 전송 실패 시 버전을 기록하지 않습니다. 다음 5분 실행 때 다시 시도합니다.
- Apps Script 시간 트리거는 즉시 실행이 아니라 수 분의 지연이 있을 수 있습니다.
