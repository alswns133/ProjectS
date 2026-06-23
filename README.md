# ProjectS

🛠️ 프로젝트 협업 가이드
이 문서는 Unity 프로젝트를 Git/GitHub로 함께 작업하기 위한 규칙입니다.
새로 합류했다면 먼저 [0. 프로젝트 초기 세팅]을 끝낸 뒤 작업을 시작하세요.
---
0. 프로젝트 초기 세팅 (처음 한 번만)
> ⚠️ 아래 1~2번은 **코드/에셋을 처음 올리기(첫 커밋) 전에** 반드시 끝내야 합니다.
> `.gitignore` 없이 한 번이라도 올리면 자동 생성 폴더가 저장소에 박혀서 나중에 빼기가 까다로워집니다.
1) `.gitignore` 적용 (1순위)
Unity는 `Library/`, `Temp/`, `obj/`, `Build/` 같은 자동 생성 폴더를 만듭니다.
이건 사람마다 내용이 다르고 용량도 커서, 올리면 충돌과 용량 폭발의 원인이 됩니다.
GitHub에서 저장소 생성 시 Add .gitignore → Unity 선택 (가장 간단)
이미 빈 저장소라면 github/gitignore의 `Unity.gitignore` 내용을 복사해 프로젝트 루트에 `.gitignore`로 저장
`Assets` 폴더의 `.meta` 파일은 절대 무시하지 말 것 (`!/[Aa]ssets/**/*.meta`) — 빠지면 팀원 간 레퍼런스가 깨집니다
평문 원본 데이터 / 세이브 폴더도 빌드에 포함되지 않도록 여기에 추가
2) Git LFS 세팅 (대용량 에셋)
이미지·사운드·모델 같은 바이너리 대용량 파일은 일반 Git이 잘 다루지 못합니다.
초반에 LFS로 잡아두지 않으면 저장소가 빠르게 비대해집니다.
```bash
git lfs install
git lfs track "*.png" "*.jpg" "*.jpeg" "*.psd" "*.tga" "*.tif" "*.exr"
git lfs track "*.fbx" "*.obj" "*.blend"
git lfs track "*.wav" "*.mp3" "*.ogg" "*.aiff"
git lfs track "*.mp4" "*.mov"
git lfs track "*.ttf" "*.otf"
git lfs track "*.cubemap" "*.unity3d"
```
위 명령으로 생성되는 `.gitattributes` 파일을 반드시 함께 커밋 (팀 전체 적용)
사운드(`wav`/`ogg`)도 포함 — 클립 파일이 곧 쌓입니다
합류한 팀원도 각자 PC에서 `git lfs install`을 한 번은 실행해야 LFS 파일이 정상으로 받아집니다
### LFS 용량 확인 (선택)
LFS 저장 공간이 얼마나 찼는지 궁금할 때 사용합니다. (무료 한도: 계정 전체 합산 1GB)

```bash
# 파일별 용량 보기
git lfs ls-files --size

# 확장자별 총합 보기 (더 간편)
git lfs migrate info

# 적용 확인 하는 용도 
git lfs track
```

> 한도는 레포별이 아니라 **계정 전체 합산**이며, 바이너리(LFS 추적 파일)만 계산됩니다.
> 무거운 에셋을 추가할 때는 정말 필요한 것만 넣어 용량을 아껴 주세요.

3) Asset Serialization 설정 (씬 머지 대비)
`Edit > Project Settings > Editor`에서:
Version Control → Mode: `Visible Meta Files` (메타 파일 노출)
Asset Serialization → Mode: `Force Text` (씬/프리팹을 텍스트로 저장해 충돌 시 병합 가능)
4) Smart Merge 등록 (각자 PC, 권장)
씬/프리팹 충돌 자동 병합 도구(UnityYAMLMerge)를 각자 로컬 Git 설정에 등록합니다.
시작할 때 함께 하면 가장 좋고, 여건이 안 되면 씬 충돌이 처음 났을 때 등록해도 됩니다.
> ✅ 초기 세팅 순서 요약
> 저장소 생성 → `.gitignore` 배치 → LFS 세팅 → **(여기서 첫 커밋/푸시)** → Force Text 확인
---
1. 브랜치(Branch) 관리
`main` 브랜치 직접 작업 절대 금지: `main`은 언제나 실행 가능한 최종 버전만 유지합니다.
개인 브랜치에서 작업: 새 기능·에셋 추가 시 반드시 본인 이름이 들어간 브랜치를 생성하세요.
규칙: `develop-이름`
예시: `develop-janghwan`, `develop-taehoon`
작업 시작 전 최신 `main` 받기: 새 작업을 시작하기 전 항상 `main`을 pull 받아 본인 브랜치에 반영(merge 또는 rebase)하세요. 옛 버전 위에서 작업하면 나중에 충돌이 커집니다.
---
2. 커밋 & 푸시
커밋 메시지는 `[분류] 내용` 형식으로 통일합니다.
예시: `[Sound] 씬 단위 프리로드 추가`, `[Fix] SFX 풀 중복 재생 버그 수정`, `[Data] 몬스터 테이블 추가`
의미 있는 단위로 자주, 작게 커밋하세요. (한 커밋에 여러 기능을 몰지 않기)
작업이 일단락되면 커밋 후 푸시합니다.
---
3. 작업 공유 (Discord)
푸시 후에는 디스코드 채널에 아래 내용을 공유해 주세요.
📸 스크린샷 또는 영상 (작업 내용 확인용)
📝 작업 내용 설명 (수정·추가된 사항)
---
4. 코드 병합 (Pull Request)
공유 후 GitHub에서 `main` 방향으로 **Pull Request(PR)**를 생성합니다.
PR은 기능 하나 단위로 잘게 만드세요. 너무 크면 리뷰가 어렵고 충돌이 늘어납니다.
리드(또는 팀원)의 코드 리뷰와 승인 후 `main`에 최종 병합됩니다.
