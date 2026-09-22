using System;
using System.Reflection;
using System.Threading.Tasks;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ProjectS.Core;
using ProjectS.Networking;

namespace ProjectS.Managers
{
    /// <summary>
    /// 인게임 → 캐릭터 선택 화면 복귀. <b>씬만 바꾸는 것이 아니라 세션을 리셋</b>한다.
    ///
    /// 게임 중에는 캐릭터에 묶인 매니저가 DontDestroyOnLoad로 여럿 살아 있다
    /// (PlayerManager와 Player 오브젝트 · InventoryManager · QuestManager · UIManager ·
    ///  SoundManager · 네트워크 매니저 · ChatManager …).
    /// 이 상태로 캐릭터 선택 씬만 로드하면:
    ///   · HUD와 팝업이 선택 화면 위에 그대로 남고,
    ///   · 조작 가능한 Player가 선택 씬을 돌아다니며,
    ///   · ★ 가장 위험하게는 <b>이전 캐릭터의 인벤토리·퀘스트가 그대로 남아</b> 다른 캐릭터로 접속했을 때
    ///     그 상태가 Firebase 세이브를 덮어쓴다(2026-09-18 장비 소실과 같은 종류의 사고).
    ///
    /// 그래서 "매니저마다 초기화 메서드를 부르는" 방식 대신, 앱을 다시 켠 것과 같은 상태로 되돌린다.
    /// 매니저가 하나 늘 때마다 초기화를 빠뜨릴 위험이 없다는 것이 이 선택의 이유다.
    ///
    /// 순서(하나라도 앞뒤가 바뀌면 데이터 사고로 이어진다):
    ///   ① 베일로 덮기 → ② 저장 완료까지 대기 → ③ 네트워크 정리 →
    ///   ④ DontDestroyOnLoad 오브젝트 파괴 → ⑤ static 상태 리셋 → ⑥ 캐릭터 선택 씬 로드
    ///
    /// <see cref="ISurvivesReboot"/>가 붙은 오브젝트만 ④에서 살아남는다(로그·오토세이브 러너 등
    /// 앱 시작에만 생성되어 다시 만들 경로가 없는 인프라).
    /// </summary>
    public static class SessionReboot
    {
        /// <summary>리부트가 진행 중인지. 중복 호출·입력을 막는 데 쓴다.</summary>
        public static bool IsRebooting { get; private set; }

        /// <summary>
        /// 기본 베일 색. 캐릭터 선택 씬 <see cref="ProjectS.UI.EntryVeil"/>의 배경색과 맞춰야
        /// 씬이 바뀌는 순간 색이 튀지 않는다.
        /// </summary>
        public static Color VeilColor = new Color(0.02f, 0.02f, 0.03f, 1f);

        /// <summary>
        /// 로고·스피너가 있는 베일 프리팹(선택). 넣으면 단색 베일 대신 이것을 띄운다.
        /// 게임 시작 시 한 번 대입해 두면 된다(예: 부트스트랩에서 <c>SessionReboot.VeilPrefab = ...</c>).
        /// </summary>
        public static GameObject VeilPrefab;

        /// <summary>
        /// 저장 → 네트워크 정리 → 세션 파괴 → 캐릭터 선택 씬 로드. UI 버튼에서 직접 부른다.
        ///
        /// 저장에 실패하면 <b>나가지 않고 중단</b>한다. 나가는 순간 이번 플레이의 진행이 사라지는데,
        /// 그 사실을 사용자가 알 방법이 없기 때문이다.
        /// </summary>
        /// <param name="sceneName">돌아갈 캐릭터 선택 씬 이름(Build Settings 등록 필요)</param>
        /// <param name="onAborted">중단됐을 때 사용자에게 보여 줄 사유를 받는 콜백(선택)</param>
        public static async void ToCharacterSelect(string sceneName = "CharacterSelect", Action<string> onAborted = null)
        {
            if (IsRebooting) return;
            IsRebooting = true;

            GameObject veil = CreateVeil();

            try
            {
                // ② 저장 — fire-and-forget이 아니라 반드시 완료를 기다린다.
                //    업로드가 끝나기 전에 ④에서 매니저를 파괴하면 그대로 유실된다.
                bool saved = await PlayerSaveService.SaveNow();
                bool hadCharacter = GameSession.SelectedCharacter != null;

                if (!saved && hadCharacter)
                {
                    // SaveNow가 스스로 막은 경우(테이블 로드 실패 등)까지 포함한다. 사유는 그쪽이 이미 로그로 남겼다.
                    Abort(veil, onAborted, "저장에 실패해 캐릭터 선택으로 돌아가지 않았습니다. 잠시 후 다시 시도해 주세요.");
                    return;
                }

                // ③ 네트워크 정리 — 파티/레이드에 남은 채 끊으면 서버에 유령 멤버가 남는다.
                await ShutdownNetwork();

                // ④ 세션에 묶인 DontDestroyOnLoad 오브젝트 파괴.
                DestroyPersistentObjects(veil);

                // Destroy는 이 프레임 끝에 반영된다. OnDestroy가 다 돌고 난 뒤에 static을 비워야
                // "이미 null인 Instance를 OnDestroy가 다시 건드리는" 순서 문제가 생기지 않는다.
                await Task.Yield();

                // ⑤ static 상태 리셋.
                ResetStatics();

                // ⑥ 캐릭터 선택 씬 로드. 그 씬의 EntryVeil이 Awake에서 화면을 덮으므로 우리 베일과 교대한다.
                AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
                while (load != null && !load.isDone) await Task.Yield();

                // 새 씬의 Awake가 끝난 다음 프레임에 우리 베일을 치운다(먼저 치우면 한 프레임 비친다).
                await Task.Yield();
            }
            catch (Exception e)
            {
                Debug.LogError($"[SessionReboot] 캐릭터 선택 복귀 중 예외: {e}");
                onAborted?.Invoke("캐릭터 선택으로 돌아가지 못했습니다.");
            }
            finally
            {
                if (veil != null) UnityEngine.Object.Destroy(veil);
                IsRebooting = false;
            }
        }

        private static void Abort(GameObject veil, Action<string> onAborted, string reason)
        {
            Debug.LogError($"[SessionReboot] 중단 — {reason}");
            if (veil != null) UnityEngine.Object.Destroy(veil);
            IsRebooting = false;
            onAborted?.Invoke(reason);
        }

        // ── ③ 네트워크 ──────────────────────────────────────────────

        // 파티 탈퇴를 먼저 알리고 연결을 끊는다. 탈퇴 통지는 best-effort다(Mirror가 다음 배치에 보내므로
        // 끊는 시점과 경합할 수 있다) — 최종 정리는 서버의 OnServerDisconnect가 책임진다.
        private static async Task ShutdownNetwork()
        {
            if (!NetworkClient.active && !NetworkServer.active) return;

            if (PartyManager.Local != null)
            {
                PartyManager.Local.RequestLeave();
                await Task.Yield();   // Cmd가 전송될 여유 한 프레임
            }

            NetworkManager manager = NetworkManager.singleton;
            if (manager != null)
            {
                if (NetworkServer.active) manager.StopHost();
                else manager.StopClient();
            }

            await Task.Yield();
        }

        // ── ④ 파괴 ─────────────────────────────────────────────────

        // DontDestroyOnLoad 씬의 루트를 훑어 파괴한다. 그 씬은 직접 얻을 수 없어서,
        // 임시 오브젝트를 하나 넣어 그 오브젝트의 scene으로 접근한다(정석 트릭).
        private static void DestroyPersistentObjects(GameObject keep)
        {
            GameObject probe = new GameObject("[RebootProbe]");
            UnityEngine.Object.DontDestroyOnLoad(probe);

            GameObject[] roots = probe.scene.GetRootGameObjects();
            UnityEngine.Object.Destroy(probe);

            foreach (GameObject root in roots)
            {
                if (root == null || root == probe || root == keep) continue;

                // 앱 시작에만 생성되는 인프라는 남긴다(파괴하면 다시 만들 경로가 없다).
                if (root.GetComponentInChildren<ISurvivesReboot>(true) != null) continue;

                UnityEngine.Object.Destroy(root);
            }
        }

        // ── ⑤ static 리셋 ───────────────────────────────────────────

        // 우리 어셈블리의 [RuntimeInitializeOnLoadMethod(SubsystemRegistration)] 메서드를 전부 다시 부른다.
        // 이는 Unity가 앱을 켤 때 하는 일과 정확히 같다 — 이벤트 허브(PlayerEvents·InventoryEvents…),
        // 레지스트리(QuestWaypointRegistry·MinimapEvents), 컨텍스트(DungeonContext·GameSession),
        // 스킬 상태 등이 한 번에 초기값으로 돌아간다.
        //
        // 이름을 일일이 적지 않는 이유는 그것이 정확히 사고가 나는 지점이기 때문이다. 이벤트 허브는
        // 지금도 30곳이 넘고, 새로 하나 추가한 사람이 이 파일까지 고쳐 주리라 기대할 수 없다.
        // 리셋을 빠뜨리면 파괴된 UI가 구독자로 남아 MissingReferenceException이 쏟아지거나,
        // 더 나쁘게는 이전 캐릭터의 값이 조용히 남는다.
        //
        // ★ SessionReboot 자신에게는 이 속성을 가진 메서드를 절대 만들지 않는다 —
        //   여기서 자기 IsRebooting을 되돌려 버린다.
        private static void ResetStatics()
        {
            Type[] types;
            try
            {
                types = typeof(SessionReboot).Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types;   // 일부 타입 로드 실패 — 읽을 수 있는 것만으로 진행한다
            }

            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            foreach (Type type in types)
            {
                if (type == null || type.IsGenericTypeDefinition) continue;

                MethodInfo[] methods;
                try
                {
                    methods = type.GetMethods(flags);
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (MethodInfo method in methods)
                {
                    if (method.GetParameters().Length > 0) continue;

                    RuntimeInitializeOnLoadMethodAttribute attribute =
                        method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();

                    // AfterSceneLoad 훅은 부르지 않는다. 그쪽에는 "서버면 부트스트랩 씬으로 보낸다" 같은
                    // 실제 동작이 섞여 있어, 다시 부르면 씬이 엉뚱한 곳으로 튄다.
                    if (attribute == null || attribute.loadType != RuntimeInitializeLoadType.SubsystemRegistration) continue;

                    try
                    {
                        method.Invoke(null, null);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[SessionReboot] {type.Name}.{method.Name} 리셋 실패: {e}");
                    }
                }
            }
        }

        // ── 베일 ───────────────────────────────────────────────────

        // 전환 내내 화면을 덮는 임시 베일. DontDestroyOnLoad로 씬을 건너 살아남고,
        // 파괴 단계에서는 제외 대상으로 넘겨 자기 자신이 지워지지 않게 한다.
        private static GameObject CreateVeil()
        {
            if (VeilPrefab != null)
            {
                GameObject instance = UnityEngine.Object.Instantiate(VeilPrefab);
                UnityEngine.Object.DontDestroyOnLoad(instance);
                return instance;
            }

            GameObject root = new GameObject("[RebootVeil]");
            UnityEngine.Object.DontDestroyOnLoad(root);

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;   // HUD·팝업·오버레이보다 확실히 위

            // 아래 캔버스의 버튼이 눌리지 않게 레이캐스터까지 붙인다(이미지만으로는 입력이 통과한다).
            root.AddComponent<GraphicRaycaster>();

            GameObject background = new GameObject("Background");
            background.transform.SetParent(root.transform, false);

            Image image = background.AddComponent<Image>();
            image.color = VeilColor;
            image.raycastTarget = true;

            RectTransform rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return root;
        }
    }
}
