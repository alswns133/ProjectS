using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Managers;

namespace ProjectS.UI
{
    /// <summary>
    /// 캐릭터 선택/생성 화면. 로그인 성공 후 켜지며, 이 계정의 캐릭터 목록을 불러와 버튼으로 나열한다.
    /// - 목록의 캐릭터 클릭 → <see cref="GameSession"/>에 담고 게임 씬(Bootstrap)으로 접속.
    /// - 이름 입력 + 타입 버튼 → 새 캐릭터 생성(전 서버 유니크 이름). 슬롯이 차면 생성 버튼이 잠긴다.
    /// 캐릭터 행 버튼은 프리팹 없이 런타임에 생성한다(listContainer에 VerticalLayoutGroup 필요).
    /// </summary>
    public class CharacterSelectUI : MonoBehaviour
    {
        private const int TypeSwordsman = 1;   // PlayerStatTable CharacterId
        private const int TypeGunner = 2;

        [Header("슬롯")]
        [SerializeField] private int maxSlots = 3;

        [Header("접속할 게임 씬(Build Settings 등록 필요)")]
        [SerializeField] private string gameSceneName = "Bootstrap";

        [Header("참조")]
        [SerializeField] private Transform listContainer;          // 캐릭터 버튼들이 쌓이는 부모(VerticalLayoutGroup)
        [SerializeField] private TMP_InputField nameField;
        [SerializeField] private Button createSwordsmanButton;
        [SerializeField] private Button createGunnerButton;
        [SerializeField] private TMP_Text messageText;

        [Header("행 폰트 (한글)")]
        [SerializeField] private TMP_FontAsset rowFont;   // 런타임 생성 행 라벨용(Paperlogy 등 한글 폰트). 비면 기본 폰트.

        [Header("뒤로 (Esc 로그아웃)")]
        [SerializeField] private GameObject authRoot;     // Esc 시 되돌아갈 로그인/가입 패널(없으면 Esc 무시)

        private readonly List<GameObject> rowObjects = new();
        private bool busy;

        private void OnEnable()
        {
            createSwordsmanButton.onClick.AddListener(OnCreateSwordsman);
            createGunnerButton.onClick.AddListener(OnCreateGunner);
            Refresh();
        }

        private void OnDisable()
        {
            createSwordsmanButton.onClick.RemoveListener(OnCreateSwordsman);
            createGunnerButton.onClick.RemoveListener(OnCreateGunner);
        }

        // Esc = 뒤로: 로그아웃하고 인증(로그인/가입) 화면으로 돌아간다.
        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Logout();
        }

        private void Logout()
        {
            if (FirebaseManager.Instance != null) FirebaseManager.Instance.Logout();
            GameSession.Clear();

            gameObject.SetActive(false);
            if (authRoot != null) authRoot.SetActive(true);
        }

        /// <summary>서버에서 캐릭터 목록을 다시 불러와 UI를 갱신한다(진입 시·생성 후).</summary>
        public async void Refresh()
        {
            SetInteractable(false);
            SetMessage("불러오는 중...");
            ClearRows();

            List<CharacterSaveData> characters = await FirebaseManager.Instance.LoadAllCharacters();
            if (this == null) return;   // 로딩 중 씬 전환됐을 수 있다

            foreach (CharacterSaveData c in characters)
                CreateRow(c);

            bool full = characters.Count >= maxSlots;
            createSwordsmanButton.interactable = !full;
            createGunnerButton.interactable = !full;

            SetMessage(full
                ? $"슬롯이 가득 찼습니다 (최대 {maxSlots})"
                : $"캐릭터 {characters.Count}/{maxSlots} — 이름을 입력하고 타입을 골라 생성하세요.");
            busy = false;
        }

        private void OnCreateSwordsman() => Create(TypeSwordsman);
        private void OnCreateGunner() => Create(TypeGunner);

        private async void Create(int type)
        {
            if (busy) return;
            busy = true;
            SetInteractable(false);
            SetMessage("생성 중...");

            CreateCharacterResult result = await FirebaseManager.Instance.CreateCharacter(type, nameField.text);
            if (this == null) return;

            switch (result)
            {
                case CreateCharacterResult.Success:
                    nameField.text = string.Empty;
                    Refresh();   // 목록 갱신(busy는 Refresh 끝에서 풀림)
                    return;
                case CreateCharacterResult.NameTaken:
                    SetMessage("이미 사용 중인 이름입니다.");
                    break;
                case CreateCharacterResult.InvalidName:
                    SetMessage("이름은 2~12자여야 합니다 ( . # $ [ ] / 사용 불가 ).");
                    break;
                default:
                    SetMessage("생성에 실패했습니다. 잠시 후 다시 시도하세요.");
                    break;
            }

            SetInteractable(true);
            busy = false;
        }

        // 선택한 캐릭터를 세션에 담고 게임 씬으로 접속한다.
        private void Enter(CharacterSaveData character)
        {
            GameSession.SetSelectedCharacter(character);
            SceneManager.LoadScene(gameSceneName);
        }

        // ── 행 버튼(런타임 생성) ───────────────────────────────────
        private void CreateRow(CharacterSaveData character)
        {
            GameObject go = new GameObject($"Row_{character.name}",
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(listContainer, false);

            LayoutElement le = go.GetComponent<LayoutElement>();
            le.minHeight = 56f;
            le.preferredHeight = 56f;   // VerticalLayoutGroup(childControlHeight=true)이 이 높이로 행을 잡는다

            Image bg = go.GetComponent<Image>();
            bg.color = new Color(0.2f, 0.32f, 0.55f, 0.9f);

            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = bg;
            CharacterSaveData captured = character;   // 클로저 캡처(루프 변수 공유 방지)
            btn.onClick.AddListener(() => Enter(captured));

            GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);
            TextMeshProUGUI label = labelGo.GetComponent<TextMeshProUGUI>();
            label.text = $"{character.name}   [{TypeName(character.characterType)} · Lv.{character.level}]";
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 22f;
            label.color = Color.white;
            TMP_FontAsset font = rowFont != null ? rowFont : TMP_Settings.defaultFontAsset;
            if (font != null) label.font = font;

            RectTransform lrt = label.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;

            rowObjects.Add(go);
        }

        private void ClearRows()
        {
            foreach (GameObject go in rowObjects)
                if (go != null) Destroy(go);
            rowObjects.Clear();
        }

        private static string TypeName(int type)
        {
            switch (type)
            {
                case TypeSwordsman: return "검사";
                case TypeGunner: return "거너";
                default: return $"타입{type}";
            }
        }

        private void SetInteractable(bool value)
        {
            createSwordsmanButton.interactable = value;
            createGunnerButton.interactable = value;
        }

        private void SetMessage(string message)
        {
            if (messageText != null) messageText.text = message;
        }
    }
}
