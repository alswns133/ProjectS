using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Mirror;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProjectS.Data;
using ProjectS.Managers;
using UnityEngine;
using UnityEngine.Networking;

namespace ProjectS.Networking
{
    /// <summary>
    /// 서버권위 스탯 파이프라인의 <b>접속 인증 게이트</b>(Mirror <see cref="NetworkAuthenticator"/>).
    /// 클라가 접속할 때 자기 <b>Firebase ID 토큰 + 고른 캐릭터 슬롯 id</b>를 서버로 넘기는 채널을 연다.
    /// 서버는 그 토큰으로 '그 유저의' 세이브만 RTDB REST로 읽어(다음 단계) 스탯을 <b>서버가 직접 도출</b>한다
    /// — 클라가 보낸 스탯을 믿지 않는다(데이터 변조를 원천 차단).
    ///
    /// <para><b>왜 Authenticator인가.</b> "서버는 클라를 믿지 않는다"를 문 앞에서 강제하려면, 플레이어
    /// 오브젝트가 스폰되기 <b>전</b>(접속 핸드셰이크)에 신원을 확정해야 한다. 스폰 후 Command로 받으면 이미
    /// 들여보낸 뒤라 게이트가 약하다. Authenticator는 핸드셰이크가 끝나야 커넥션이 준비되므로 여기가 맞다.</para>
    ///
    /// <para><b>단계.</b> 이 파일은 <b>S1(채널)</b>만 담는다 — 클라가 토큰+id를 보내고, 서버가 받아 커넥션에
    /// 보관하고 <b>무조건 수락</b>한다. 그래서 기존 접속 흐름(에디터 Host·오프라인 테스트 포함)을 하나도 깨지
    /// 않는다. 실제 REST 세이브 읽기·검증·거부는 <b>S2</b>에서 <see cref="OnAuthRequest"/>에 채운다.</para>
    /// </summary>
    [AddComponentMenu("Network/ Authenticators/Firebase Server Authenticator")]
    public class FirebaseServerAuthenticator : NetworkAuthenticator
    {
        [Header("서버 설정 (REST 세이브 읽기)")]
        [Tooltip("Realtime Database URL. google-services.json의 databaseURL과 같은 값 " +
                 "(예: https://프로젝트-default-rtdb.firebaseio.com). 서버가 이 주소의 REST로 세이브를 읽는다. " +
                 "끝 슬래시는 있어도 없어도 됨. 서버는 Firebase SDK가 꺼져 있어 이 값을 인스펙터로 직접 받는다.")]
        [SerializeField] private string databaseUrl = "";

        [Tooltip("켜면 유효한 토큰으로 세이브를 못 읽은 커넥션을 '거부'한다(운영). " +
                 "끄면(개발 기본) 토큰이 없거나 실패해도 접속을 허용하되 서버 세이브 없이(save=null) 들어온다 " +
                 "— 에디터 Host·오프라인 테스트를 막지 않기 위함. ★운영 배포 전 반드시 켠다.")]
        [SerializeField] private bool requireAuth = false;

        [Tooltip("REST 요청 타임아웃(초). 초과하면 실패로 처리(requireAuth면 거부, 아니면 save 없이 허용).")]
        [SerializeField] private int restTimeoutSeconds = 10;

        #region Messages

        /// <summary>클라 → 서버. 접속 인증 요청. 서버는 <see cref="idToken"/>으로 세이브를 읽고(S2), 스탯을 도출한다.</summary>
        public struct AuthRequestMessage : NetworkMessage
        {
            /// <summary>Firebase ID 토큰. 서버가 RTDB REST(<c>?auth=</c>)로 '이 유저' 세이브만 읽는 열쇠.</summary>
            public string idToken;

            /// <summary>접속에 사용할 캐릭터 슬롯의 uniqueId(Users/{uid}/Characters/{id}). 어느 캐릭인지만 지목한다.</summary>
            public long characterId;
        }

        /// <summary>서버 → 클라. 인증 결과.</summary>
        public struct AuthResponseMessage : NetworkMessage
        {
            /// <summary>100=수락, 200=거부.</summary>
            public byte code;

            /// <summary>사람이 읽을 사유(거부 시 로그·UI 힌트).</summary>
            public string message;
        }

        #endregion

        #region Server-side pending store

        /// <summary>
        /// 한 커넥션의 인증 정보. 서버가 커넥션별로 들고 있다가, 플레이어 스폰·스탯 도출이 이걸 읽는다.
        /// S1은 <see cref="idToken"/>/<see cref="characterId"/>만 채우고, S2가 <see cref="uid"/>/<see cref="save"/>를 채운다.
        /// class(참조형)라 S2가 같은 항목을 교체 없이 채워 넣을 수 있다.
        /// </summary>
        public class PendingConnectionAuth
        {
            public string idToken;
            public long characterId;

            // ── S2에서 채움(REST 읽기 결과) ──
            public string uid;               // 토큰이 가리키는 유저(세이브 경로 확정용)
            public CharacterSaveData save;   // 서버가 REST로 읽은 '권위' 세이브(스탯 도출 입력)

            // ── S3에서 채움(세이브로 도출한 전투 스탯) ──
            public CombatStatBlock stats;    // 서버가 소유하는 권위 전투 스탯(다음 단계에서 클라로 복제·데미지 계산에 사용)
        }

        // 커넥션 → 인증 정보. 서버 전용. static이라 스폰 훅·스탯 도출 등 다른 서버 코드가 조회한다.
        private static readonly Dictionary<NetworkConnectionToClient, PendingConnectionAuth> pending = new();

        /// <summary>이 커넥션의 인증 정보를 조회한다(스폰 훅·스탯 도출이 사용). 없으면 false.</summary>
        public static bool TryGet(NetworkConnectionToClient conn, out PendingConnectionAuth auth)
            => pending.TryGetValue(conn, out auth);

        /// <summary>접속 종료 시 커넥션 인증 정보를 지운다. 장수명 전용 서버에서 누수를 막는다(GameNetworkManager.OnServerDisconnect가 호출).</summary>
        public static void RemovePending(NetworkConnectionToClient conn) => pending.Remove(conn);

        #endregion

        #region Server

        public override void OnStartServer()
        {
            NetworkServer.RegisterHandler<AuthRequestMessage>(OnAuthRequest, false);
        }

        public override void OnStopServer()
        {
            NetworkServer.UnregisterHandler<AuthRequestMessage>();
            pending.Clear();
        }

        // 클라 접속 시 호출. 우리는 클라의 AuthRequestMessage를 기다린다(BasicAuthenticator와 같은 결).
        public override void OnServerAuthenticate(NetworkConnectionToClient conn) { }

        // 클라의 인증 요청 도착. 토큰으로 그 유저 세이브를 REST로 읽어(권위 스탯의 입력) 성공하면 수락한다.
        // ★ ServerAccept/Reject는 REST가 끝난 뒤(코루틴) 불린다 — Mirror가 지원하는 async 수락 패턴.
        private void OnAuthRequest(NetworkConnectionToClient conn, AuthRequestMessage msg)
        {
            PendingConnectionAuth auth = new PendingConnectionAuth
            {
                idToken = msg.idToken,
                characterId = msg.characterId,
            };
            pending[conn] = auth;

            Debug.Log($"[Auth] 인증 요청 수신 conn={conn.connectionId}, charId={msg.characterId}, " +
                      $"token={(string.IsNullOrEmpty(msg.idToken) ? "없음(오프라인/미로그인)" : $"있음({msg.idToken.Length}자)")}");

            // 토큰이 없다(오프라인/미로그인/에디터 dev). 운영이면 거부, 개발이면 세이브 없이 허용(기존 동작 유지).
            if (string.IsNullOrEmpty(msg.idToken))
            {
                if (requireAuth) Reject(conn, "로그인 토큰이 없습니다.");
                else AcceptDev(conn, "토큰 없음 → 개발 허용(save=null)");
                return;
            }

            // 토큰에서 uid를 뽑는다(서명 검증이 아니라 경로 확정용 — 위조 차단은 아래 REST 읽기를 DB 규칙이 막아준다).
            if (!TryGetUidFromToken(msg.idToken, out string uid))
            {
                if (requireAuth) Reject(conn, "토큰 형식이 올바르지 않습니다.");
                else AcceptDev(conn, "토큰 파싱 실패 → 개발 허용(save=null)");
                return;
            }
            auth.uid = uid;

            if (string.IsNullOrWhiteSpace(databaseUrl))
            {
                // 서버 설정 누락. 개발이면 통과시키되 경고로 알린다.
                Debug.LogWarning("[Auth] databaseUrl 미설정 → REST 읽기 불가. FirebaseServerAuthenticator 인스펙터에 DB URL을 넣어라.", this);
                if (requireAuth) Reject(conn, "서버 설정 오류(databaseUrl).");
                else AcceptDev(conn, "databaseUrl 미설정 → 개발 허용(save=null)");
                return;
            }

            StartCoroutine(LoadSaveThenDecide(conn, auth));
        }

        // 토큰으로 Users/{uid}/Characters/{characterId} 를 REST GET → 세이브 파싱 → 수락/거부.
        private IEnumerator LoadSaveThenDecide(NetworkConnectionToClient conn, PendingConnectionAuth auth)
        {
            string url = $"{databaseUrl.TrimEnd('/')}/Users/{auth.uid}/Characters/{auth.characterId}.json?auth={UnityWebRequest.EscapeURL(auth.idToken)}";

            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.timeout = Mathf.Max(1, restTimeoutSeconds);
                yield return req.SendWebRequest();

                // 커넥션이 그 사이 끊겼으면 조용히 끝낸다(끊긴 커넥션에 Accept/Reject 금지).
                if (!pending.ContainsKey(conn)) yield break;

                if (req.result != UnityWebRequest.Result.Success)
                {
                    // 401/권한거부(토큰이 그 uid 것이 아님) 포함. 위조 토큰은 여기서 걸린다.
                    Debug.LogWarning($"[Auth] 세이브 REST 실패 conn={conn.connectionId}, uid={auth.uid}, " +
                                     $"result={req.result}, http={req.responseCode}, err={req.error}");
                    if (requireAuth) Reject(conn, "세이브 인증/조회 실패.");
                    else AcceptDev(conn, "REST 실패 → 개발 허용(save=null)");
                    yield break;
                }

                string body = req.downloadHandler.text;

                // RTDB는 노드가 없으면 리터럴 "null"을 준다(존재하지 않는 캐릭터 id).
                if (string.IsNullOrWhiteSpace(body) || body == "null")
                {
                    Debug.LogWarning($"[Auth] 세이브 없음 conn={conn.connectionId}, uid={auth.uid}, charId={auth.characterId}");
                    if (requireAuth) Reject(conn, "선택한 캐릭터를 찾을 수 없습니다.");
                    else AcceptDev(conn, "세이브 없음 → 개발 허용(save=null)");
                    yield break;
                }

                CharacterSaveData save;
                try
                {
                    save = JsonConvert.DeserializeObject<CharacterSaveData>(body);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Auth] 세이브 파싱 실패 conn={conn.connectionId}: {ex}");
                    if (requireAuth) Reject(conn, "세이브 데이터 손상.");
                    else AcceptDev(conn, "세이브 파싱 실패 → 개발 허용(save=null)");
                    yield break;
                }

                // 노드 키(characterId)를 정체성의 진실로 고정한다(FirebaseManager.LoadCharacter와 같은 방침 —
                // uniqueId가 Ticks라 JS 계층 정밀도로 깎일 수 있어 요청 id로 덮는다).
                if (save != null) save.uniqueId = auth.characterId;
                auth.save = save;

                // 세이브로 전투 스탯을 서버가 직접 도출해 보관한다(권위). 다음 단계에서 클라로 복제·데미지 계산에 쓴다.
                auth.stats = ServerStatDeriver.Derive(save);

                Debug.Log($"[Auth] 세이브 로드 성공 conn={conn.connectionId}, uid={auth.uid}, " +
                          $"char='{save?.name}', lv={save?.level}, type={save?.characterType} → 스탯[{auth.stats}]");

                conn.Send(new AuthResponseMessage { code = 100, message = "OK" });
                ServerAccept(conn);
            }
        }

        // 개발 허용(save 없이). 서버가 그 커넥션의 권위 스탯을 모르는 상태 → 다음 단계에서 기존 값(클라 제공)으로 폴백.
        private void AcceptDev(NetworkConnectionToClient conn, string reason)
        {
            Debug.Log($"[Auth] 수락(개발) conn={conn.connectionId} — {reason}");
            conn.Send(new AuthResponseMessage { code = 100, message = "OK(dev)" });
            ServerAccept(conn);
        }

        // 거부. 사유 메시지를 먼저 보내고 잠시 뒤 끊는다(메시지가 도착하도록 — BasicAuthenticator와 같은 방침).
        private void Reject(NetworkConnectionToClient conn, string reason)
        {
            Debug.LogWarning($"[Auth] 거부 conn={conn.connectionId} — {reason}");
            conn.Send(new AuthResponseMessage { code = 200, message = reason });
            conn.isAuthenticated = false;
            StartCoroutine(DelayedDisconnect(conn, 1f));
        }

        private IEnumerator DelayedDisconnect(NetworkConnectionToClient conn, float waitTime)
        {
            yield return new WaitForSeconds(waitTime);
            ServerReject(conn);
        }

        // ── Firebase ID 토큰(JWT)에서 uid 추출 ──
        // JWT = header.payload.signature(각 base64url). payload를 디코드해 user_id(=uid, sub와 동일)를 읽는다.
        // ★ 서명 검증은 하지 않는다 — 필요 없다. 위조 토큰은 아래 REST(?auth=)를 DB 보안규칙(auth.uid==$uid)이
        //   거부하므로, 여기서는 '어느 경로를 읽을지'만 정하면 된다.
        private static bool TryGetUidFromToken(string token, out string uid)
        {
            uid = null;
            if (string.IsNullOrEmpty(token)) return false;

            string[] parts = token.Split('.');
            if (parts.Length < 2) return false;

            try
            {
                string json = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
                JObject payload = JObject.Parse(json);
                uid = (string)(payload["user_id"] ?? payload["sub"]);
                return !string.IsNullOrEmpty(uid);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Auth] 토큰 payload 파싱 실패: {ex}");
                return false;
            }
        }

        private static byte[] Base64UrlDecode(string input)
        {
            string s = input.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)   // base64 패딩 복원
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }

        #endregion

        #region Client

        public override void OnStartClient()
        {
            NetworkClient.RegisterHandler<AuthResponseMessage>(OnAuthResponse, false);
        }

        public override void OnStopClient()
        {
            NetworkClient.UnregisterHandler<AuthResponseMessage>();
        }

        // 클라 접속 시 호출. 토큰 획득이 비동기라, 받아온 뒤 요청을 보낸다.
        // ★ 어떤 경우든 반드시 한 번은 Send 해야 한다 — 안 보내면 서버가 핸드셰이크를 기다리며 접속이 멈춘다.
        //   그래서 실패(토큰 없음/미로그인/오프라인 테스트)해도 빈 토큰으로 보낸다(S1은 서버가 무조건 수락).
        public override void OnClientAuthenticate()
        {
            _ = SendAuthRequestAsync();
        }

        private async Task SendAuthRequestAsync()
        {
            string token = null;
            long characterId = 0;

            try
            {
                if (FirebaseManager.Instance != null)
                    token = await FirebaseManager.Instance.GetIdTokenAsync();

                CharacterSaveData selected = GameSession.SelectedCharacter;
                if (selected != null) characterId = selected.uniqueId;
            }
            catch (System.Exception ex)
            {
                // 토큰 준비 실패도 접속을 막지 않는다(빈 토큰으로 진행 → S2에서 서버가 거부 판단).
                Debug.LogError($"[Auth] 인증 요청 준비 실패(빈 토큰으로 진행): {ex}");
            }

            NetworkClient.Send(new AuthRequestMessage
            {
                idToken = token ?? string.Empty,
                characterId = characterId,
            });
        }

        private void OnAuthResponse(AuthResponseMessage msg)
        {
            if (msg.code == 100)
            {
                ClientAccept();
            }
            else
            {
                Debug.LogError($"[Auth] 서버 인증 거부: {msg.message}");
                ClientReject();
            }
        }

        #endregion
    }
}
