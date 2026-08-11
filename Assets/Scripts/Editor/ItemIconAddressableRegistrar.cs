// Editor 폴더 안에 있어 플레이어 빌드에는 포함되지 않는다(MinimapBoundsTool과 같은 규칙).
// 네임스페이스도 UnityEditor.Editor 가림을 피해 EditorTools를 쓴다.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 아이템 아이콘 스프라이트를 어드레서블에 일괄 등록하는 도구.
    /// 주소는 파일명(확장자 제외)을 그대로 쓴다 — ItemData.IconAddress와 파일명을 같게 유지하기로 했기 때문에,
    /// 손으로 30개를 등록하며 오타를 내는 것보다 파일명을 단일 원천으로 삼는 편이 안전하다.
    /// 등록 후에는 ItemData.json이 참조하는 주소와 대조해, 아이콘이 없는 주소와 쓰이지 않는 파일을 함께 보고한다.
    /// (아이콘이 없는 주소는 ItemIconLoader가 조용히 null을 돌려주므로 런타임에선 원인을 알기 어렵다.)
    /// 폴더 밖의 다른 에셋이 같은 주소를 선점하고 있는 경우도 함께 보고한다 — 어드레서블은 주소 중복을
    /// 막지 않아 옛 에셋 엔트리가 그대로 남고, 그러면 어느 쪽이 로드될지 보장되지 않는다.
    /// 메뉴: ProjectS > Addressables > 아이템 아이콘 등록.
    /// </summary>
    public class ItemIconAddressableRegistrar : EditorWindow
    {
        private const string DefaultIconFolder = "Assets/Textures/UI/ItemIcons";
        private const string DefaultGroupName = "Spite Local Group";
        private const string DefaultLabel = "sprite";
        private const string ItemDataPath = "Assets/JsonData/ItemData.json";

        private string iconFolder = DefaultIconFolder;
        private string groupName = DefaultGroupName;
        private string label = DefaultLabel;

        // 등록 없이 결과만 보고 싶을 때. 처음 돌릴 때 무엇이 바뀌는지 먼저 확인하라고 기본 true.
        private bool dryRun = true;

        private Vector2 scroll;
        private string report = string.Empty;

        [MenuItem("ProjectS/Addressables/아이템 아이콘 등록")]
        private static void Open()
        {
            GetWindow<ItemIconAddressableRegistrar>("아이템 아이콘 등록").minSize = new Vector2(460f, 380f);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("대상", EditorStyles.boldLabel);
            iconFolder = EditorGUILayout.TextField("아이콘 폴더", iconFolder);
            groupName = EditorGUILayout.TextField("어드레서블 그룹", groupName);
            label = EditorGUILayout.TextField("라벨", label);

            EditorGUILayout.Space();
            dryRun = EditorGUILayout.ToggleLeft("미리보기만 (등록하지 않음)", dryRun);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(iconFolder)))
            {
                if (GUILayout.Button(dryRun ? "검사" : "등록", GUILayout.Height(28f))) Run();
            }

            if (GUILayout.Button("ItemData.json과 대조만")) report = BuildCrossCheck();

            EditorGUILayout.Space();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void Run()
        {
            var lines = new List<string>();

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                report = "어드레서블 설정이 없습니다. Window > Asset Management > Addressables > Groups에서 먼저 초기화하세요.";
                return;
            }

            if (!AssetDatabase.IsValidFolder(iconFolder))
            {
                report = $"폴더를 찾을 수 없습니다: {iconFolder}";
                return;
            }

            AddressableAssetGroup group = settings.FindGroup(groupName);
            if (group == null)
            {
                report = $"'{groupName}' 그룹이 없습니다. Addressables Groups 창에서 만들거나 그룹명을 고치세요.";
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { iconFolder });
            if (guids.Length == 0)
            {
                report = $"{iconFolder} 안에 텍스처가 없습니다.";
                return;
            }

            // 파일명이 곧 주소라, 다른 폴더에 같은 이름이 있으면 서로를 덮어쓴다. 등록 전에 막는다.
            var byAddress = new Dictionary<string, List<string>>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string address = Path.GetFileNameWithoutExtension(path);
                if (!byAddress.TryGetValue(address, out List<string> paths))
                {
                    paths = new List<string>();
                    byAddress[address] = paths;
                }
                paths.Add(path);
            }

            List<string> duplicated = byAddress.Where(p => p.Value.Count > 1).Select(p => p.Key).ToList();
            if (duplicated.Count > 0)
            {
                lines.Add("!! 파일명이 겹칩니다. 주소가 충돌하므로 등록을 중단합니다.");
                foreach (string address in duplicated)
                {
                    lines.Add($"  {address}");
                    foreach (string path in byAddress[address]) lines.Add($"    - {path}");
                }
                report = string.Join("\n", lines);
                return;
            }

            // 어드레서블은 주소 고유성을 강제하지 않는다. 엔트리는 GUID 기준이라, 다른 파일이 같은 주소를
            // 이미 쓰고 있으면 덮어쓰기가 아니라 "같은 주소를 가진 엔트리 2개"가 된다.
            // 그 상태에서 LoadAssetAsync는 로케이터 순서로 아무거나 하나를 집으므로 옛 아이콘이 뜰 수 있다.
            // 등록 대상 폴더 밖(예: 은퇴시킬 외부 에셋)에 남아 있는 같은 주소를 미리 찾아 알린다.
            var occupiedElsewhere = new Dictionary<string, List<AddressableAssetEntry>>();
            foreach (AddressableAssetGroup g in settings.groups)
            {
                if (g == null) continue;
                foreach (AddressableAssetEntry e in g.entries)
                {
                    if (e == null || string.IsNullOrEmpty(e.address)) continue;
                    if (!byAddress.ContainsKey(e.address)) continue;

                    string entryPath = AssetDatabase.GUIDToAssetPath(e.guid);
                    if (!string.IsNullOrEmpty(entryPath) && entryPath.StartsWith(iconFolder + "/", StringComparison.Ordinal))
                        continue;   // 이번에 등록할 폴더 안의 것은 정상 대상

                    if (!occupiedElsewhere.TryGetValue(e.address, out List<AddressableAssetEntry> list))
                    {
                        list = new List<AddressableAssetEntry>();
                        occupiedElsewhere[e.address] = list;
                    }
                    list.Add(e);
                }
            }

            int added = 0, readdressed = 0, unchanged = 0;
            var notSprite = new List<string>();

            foreach (KeyValuePair<string, List<string>> pair in byAddress.OrderBy(p => p.Key))
            {
                string address = pair.Key;
                string path = pair.Value[0];

                // Sprite로 임포트되지 않은 텍스처는 ItemIconLoader의 LoadAssetAsync<Sprite>가 못 찾는다.
                if (AssetImporter.GetAtPath(path) is TextureImporter importer &&
                    importer.textureType != TextureImporterType.Sprite)
                {
                    notSprite.Add(path);
                }

                string guid = AssetDatabase.AssetPathToGUID(path);
                AddressableAssetEntry existing = settings.FindAssetEntry(guid);

                if (existing == null)
                {
                    lines.Add($"[추가] {address}  ({path})");
                    added++;
                }
                else if (existing.address != address)
                {
                    lines.Add($"[주소변경] {existing.address} -> {address}");
                    readdressed++;
                }
                else
                {
                    unchanged++;
                    continue;
                }

                if (dryRun) continue;

                AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, false);
                entry.address = address;
                if (!string.IsNullOrWhiteSpace(label))
                {
                    settings.AddLabel(label, false);
                    entry.SetLabel(label, true, false, false);
                }

                settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true, false);
            }

            if (!dryRun)
            {
                AssetDatabase.SaveAssets();
            }

            lines.Insert(0, dryRun
                ? $"[미리보기] 추가 {added} · 주소변경 {readdressed} · 그대로 {unchanged}"
                : $"[등록 완료] 추가 {added} · 주소변경 {readdressed} · 그대로 {unchanged}");

            if (occupiedElsewhere.Count > 0)
            {
                lines.Add("");
                lines.Add($"!! 주소 중복 {occupiedElsewhere.Count}건 — 폴더 밖의 다른 에셋이 같은 주소를 쓰고 있습니다.");
                lines.Add("   어드레서블은 주소 중복을 막지 않아 둘 다 남고, 어느 쪽이 로드될지 보장되지 않습니다.");
                lines.Add("   Addressables Groups 창에서 아래 엔트리를 Remove 하세요(에셋은 지워지지 않습니다).");
                foreach (KeyValuePair<string, List<AddressableAssetEntry>> pair in occupiedElsewhere.OrderBy(p => p.Key))
                {
                    lines.Add($"  {pair.Key}");
                    foreach (AddressableAssetEntry e in pair.Value)
                        lines.Add($"    - {AssetDatabase.GUIDToAssetPath(e.guid)}  [{e.parentGroup?.Name}]");
                }
            }

            if (notSprite.Count > 0)
            {
                lines.Add("");
                lines.Add("!! Sprite로 임포트되지 않은 텍스처 (인스펙터에서 Texture Type을 Sprite로 바꾸세요)");
                foreach (string path in notSprite) lines.Add($"  - {path}");
            }

            lines.Add("");
            lines.Add(BuildCrossCheck());
            report = string.Join("\n", lines);
        }

        /// <summary>
        /// ItemData.json이 참조하는 IconAddress와 폴더의 파일명을 양방향으로 대조한 결과 문자열을 만든다.
        /// </summary>
        /// <returns>사람이 읽을 대조 결과</returns>
        private string BuildCrossCheck()
        {
            if (!File.Exists(ItemDataPath)) return $"{ItemDataPath}를 찾을 수 없어 대조를 건너뜁니다.";

            HashSet<string> referenced;
            try
            {
                // JsonUtility는 최상위 배열을 못 읽어 감싸준다. 여기선 IconAddress만 필요하므로
                // enum이 있는 ProjectS.Data.ItemData 대신 최소 필드 클래스를 쓴다(enum은 문자열 파싱이 안 됨).
                string json = "{\"rows\":" + File.ReadAllText(ItemDataPath) + "}";
                IconRefList parsed = JsonUtility.FromJson<IconRefList>(json);
                referenced = new HashSet<string>(parsed.rows
                    .Select(r => r.IconAddress)
                    .Where(a => !string.IsNullOrWhiteSpace(a)));
            }
            catch (Exception e)
            {
                return $"ItemData.json 파싱 실패: {e.Message}";
            }

            var files = new HashSet<string>();
            if (AssetDatabase.IsValidFolder(iconFolder))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { iconFolder }))
                    files.Add(Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid)));
            }

            List<string> missing = referenced.Where(a => !files.Contains(a)).OrderBy(a => a).ToList();
            List<string> unused = files.Where(f => !referenced.Contains(f)).OrderBy(f => f).ToList();

            var lines = new List<string>
            {
                $"--- ItemData.json 대조 (참조 주소 {referenced.Count}종 · 폴더 파일 {files.Count}개) ---"
            };

            if (missing.Count == 0 && unused.Count == 0)
            {
                lines.Add("전부 일치합니다.");
                return string.Join("\n", lines);
            }

            if (missing.Count > 0)
            {
                lines.Add($"아이콘 파일이 없는 주소 {missing.Count}개 (인벤에서 아이콘이 빈칸으로 보임)");
                foreach (string address in missing) lines.Add($"  - {address}");
            }

            if (unused.Count > 0)
            {
                lines.Add($"ItemData가 쓰지 않는 파일 {unused.Count}개 (파일명 오타이거나 아직 미사용)");
                foreach (string file in unused) lines.Add($"  - {file}");
            }

            return string.Join("\n", lines);
        }

        [Serializable]
        private class IconRefRow
        {
            public int Index;
            public string Name;
            public string IconAddress;
        }

        [Serializable]
        private class IconRefList
        {
            public IconRefRow[] rows;
        }
    }
}
