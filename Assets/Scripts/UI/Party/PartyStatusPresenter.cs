using UnityEngine;

namespace ProjectS.UI
{
    /// <summary>
    /// 파티 상태원(<see cref="IPartySource"/>)과 파티원 상태 HUD(<see cref="PartyStatusView"/>)를 잇는 다리.
    /// "파티원이 있으면 슬롯을 채우고, 그 HP/SG를 매 프레임 게이지에 밀어 넣는다."
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>지금까지 이 배선이 없어 뷰가 비어 있었다.</b> <see cref="PartyStatusView.SetMember"/>·
    /// <see cref="PartyStatusView.SetHp"/>를 부르는 곳이 프로젝트에 없어, 껍데기만 있고 아무도 켜지 않았다.
    /// 이 컴포넌트가 소스와 뷰를 물려 처음으로 살아난다.
    /// </para>
    /// <para>
    /// <b>구성과 값을 다른 신호로 가른다.</b> 누가 파티원인가·이름·레벨은 자주 안 바뀌므로
    /// <see cref="IPartySource.OnChanged"/>로만 다시 그린다. 반면 HP/SG는 전투 중 계속 바뀌는데,
    /// 이를 OnChanged로 흘리면 로스터·결성창까지 통째로 다시 그려진다(IPartySource가 경계한 지점).
    /// 그래서 값은 여기 <see cref="Update"/>에서 직접 읽어 간다(AcceptPopup이 RemainingSeconds를
    /// 자기 Update에서 읽는 것과 같은 방식).
    /// </para>
    /// <para>
    /// <b>소스와 같은 수명의 오브젝트에 둔다.</b> 소스(NetworkPartySource/DummyPartySource)는 씬을 넘어
    /// 사는데, 이 다리가 던전 입장 창 안에 있으면 창이 닫힐 때 꺼져 파티원 게이지가 멈춘다.
    /// HUD 캔버스처럼 전투 중 늘 켜져 있는 곳에 붙인다.
    /// </para>
    /// </remarks>
    public class PartyStatusPresenter : MonoBehaviour
    {
        [Header("연결")]
        [Tooltip("그릴 대상 HUD. 비우면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private PartyStatusView view;

        [Tooltip("파티 상태를 물어볼 곳(NetworkPartySource·DummyPartySource). "
               + "비우면 등록된 파티 소스(PartySourceProvider)를 자동으로 받는다.")]
        [SerializeField] private MonoBehaviour partySourceBehaviour;

        // 파티는 2인이라 상대는 슬롯 0 하나뿐이다(PartyStatusView.MaxOtherMembers == 1).
        private const int PartnerSlot = 0;

        private IPartySource source;

        // 지금 슬롯에 파티원을 그려 뒀는지. 값 push(Update)는 파티원이 있을 때만 돈다.
        private bool hasPartner;

        // 마지막으로 게이지에 그린 값. 같은 값을 매 프레임 다시 쓰면 % 문자열이 매번 새로 할당된다.
        // 바뀔 때만 그려 그 낭비를 막는다. 새 파티원이 앉을 때 -1로 눕혀 첫 값은 반드시 그린다.
        private float shownHp = -1f;
        private float shownSg = -1f;

        private void Awake()
        {
            // 뷰는 이 오브젝트나 자식에서 찾는다. ★ includeInactive=true가 중요하다 — 자식 뷰는 "파티원이
            // 없을 때 스스로 꺼지는" 오브젝트라 솔로로 시작하면 비활성이고, 그때 못 찾으면 영영 못 붙는다.
            // ★ 프레젠터는 뷰와 다른(항상 켜진) 부모에 둔다 — 뷰가 자기 자신을 끄면 같은 오브젝트의
            //   프레젠터까지 꺼져 OnEnable이 안 돌기 때문이다(그래서 GetComponent가 아니라 InChildren).
            if (view == null) view = GetComponentInChildren<PartyStatusView>(true);
        }

        private void OnEnable()
        {
            // 인스펙터로 소스를 직접 꽂았으면 그것을, 비워 뒀으면 등록된 소스(PartySourceProvider)를 받는다.
            // ★ 후자는 소스가 이 프레젠터보다 늦게 켜질 수 있어(활성화 순서 보장 없음) Changed로 뒤늦게 붙어야
            //   한다 — 안 그러면 OnEnable 순간에 Current가 null이면 영영 안 붙는다(PartyWindowOpener와 같은
            //   late-bind, PartySourceProvider 주석 참고). NetworkPartySource는 스스로 등록하지만
            //   DummyPartySource는 등록하지 않으므로, 더미로 테스트할 땐 partySourceBehaviour에 직접 꽂는다.
            if (partySourceBehaviour != null)
            {
                Bind(partySourceBehaviour as IPartySource);
            }
            else
            {
                PartySourceProvider.Changed += OnProviderChanged;
                Bind(PartySourceProvider.Current);
            }
        }

        private void OnDisable()
        {
            PartySourceProvider.Changed -= OnProviderChanged;
            Unbind();
        }

        // 등록된 소스가 바뀌면(뒤늦게 켜짐·교체) 그때 붙는다.
        private void OnProviderChanged() => Bind(PartySourceProvider.Current);

        // 소스를 갈아 끼운다. 같은 소스면 중복 구독을 피해 그냥 둔다.
        private void Bind(IPartySource next)
        {
            if (ReferenceEquals(source, next)) return;

            Unbind();
            source = next;

            // [진단] 어느 소스에 붙었는지(또는 아직 없는지) 남긴다. 원인 파악 후 이 로그 삭제.
            Debug.Log($"[진단][PartyStatusPresenter] Bind → source={(next as MonoBehaviour != null ? $"{((MonoBehaviour)next).name}#{((MonoBehaviour)next).GetInstanceID()}" : (next != null ? next.GetType().Name : "null(late-bind 대기)"))}, view={(view != null)}", this);

            if (source == null) return;   // 아직 소스가 없다(late-bind 대기). 나중에 Changed로 다시 온다.

            source.OnChanged += Redraw;
            Redraw();
        }

        private void Unbind()
        {
            if (source != null) source.OnChanged -= Redraw;
            source = null;
            hasPartner = false;
        }

        // 구성 변화: 파티원이 생겼으면 슬롯을 채우고, 없어졌으면 뷰를 통째로 비운다.
        private void Redraw()
        {
            if (view == null || source == null) return;

            PartyMemberInfo partner = source.Partner;
            hasPartner = partner != null;

            // [진단] 파티원을 찾았는지·값이 어떤지 남긴다. 원인 파악 후 이 로그 삭제.
            Debug.Log($"[진단][PartyStatusPresenter] Redraw → partner={(partner != null ? $"{partner.Nickname}(Lv.{partner.Level})" : "null(파티 아님/미성립)")}, " +
                      $"hp={source.PartnerHpRatio:0.00}, sg={source.PartnerSgRatio:0.00}", this);

            if (!hasPartner)
            {
                view.ClearParty();
                return;
            }

            // 초상화는 직업 아이콘 매핑이 붙기 전까지 null(프리팹 기본값 유지). 이름·레벨만 채운다.
            view.SetMember(PartnerSlot, partner.Nickname, partner.Level);

            // 새 파티원이 앉았으니 첫 값을 반드시 그리게 기준을 눕히고, 붙자마자 현재 HP/SG를 한 번 민다
            // (다음 Update를 기다리지 않게).
            shownHp = -1f;
            shownSg = -1f;
            PushVitals();
        }

        private void Update()
        {
            if (!hasPartner) return;
            PushVitals();
        }

        // 원격 파티원의 비율을 게이지에 민다. 값은 계약(IPartySource)에서만 읽어 UI가 미러를 모른다.
        // 바뀐 값만 그려 % 문자열의 매 프레임 할당을 피한다.
        private void PushVitals()
        {
            float hp = source.PartnerHpRatio;
            float sg = source.PartnerSgRatio;

            if (!Mathf.Approximately(hp, shownHp))
            {
                shownHp = hp;
                view.SetHp(PartnerSlot, hp);
            }

            if (!Mathf.Approximately(sg, shownSg))
            {
                shownSg = sg;
                view.SetSg(PartnerSlot, sg);
            }
        }
    }
}
