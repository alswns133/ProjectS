using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;
using UnityEngine.Video;

namespace ProjectS.UI.Framework
{
    /// <summary>
    /// 어드레서블 주소로 VideoClip을 불러 RawImage에 반복 재생하는 UI 위젯.
    /// 스킬창 소개 영상·클래스 선택 소개 영상처럼 "창이 떠 있는 동안만 필요한 영상"용이다.
    /// </summary>
    /// <remarks>
    /// 같은 오브젝트에 RawImage + VideoPlayer를 둔다. RenderTexture는 클립 해상도에 맞춰 런타임에 만들므로
    /// 에셋을 따로 준비할 필요가 없다(인스펙터에 Target Texture를 넣어 두면 그것을 그대로 쓴다).
    /// 핸들은 지금 재생 중인 클립 하나만 쥐고, <see cref="Stop"/>·비활성화·파괴 때 Release한다.
    /// </remarks>
    [RequireComponent(typeof(RawImage), typeof(VideoPlayer))]
    public class AddressableVideoView : MonoBehaviour
    {
        [Tooltip("끝나면 처음부터 다시 재생한다. 소개 영상은 보통 반복.")]
        [SerializeField] private bool loop = true;

        [Tooltip("영상 소리를 낸다. 소개 영상은 무음이 기본(클립 임포트에서 Import Audio도 끄는 것을 권장).")]
        [SerializeField] private bool playAudio;

        private RawImage view;
        private VideoPlayer player;
        private RenderTexture runtimeTexture;   // 우리가 만든 RT만 여기에 둔다(인스펙터 지정 RT는 해제하지 않음)

        // 요청 번호는 선택을 빠르게 바꿀 때 늦게 도착한 옛 요청을 버리기 위함이다 — 주소 비교만으로는
        // A→B→A처럼 같은 주소로 돌아오면 두 요청이 모두 통과해 앞선 핸들이 새어 나간다.
        private AsyncOperationHandle<VideoClip> handle;
        private int requestId;

        /// <summary>영상이 준비돼 화면에 표시되고 있는가.</summary>
        public bool IsShowing => view != null && view.enabled;

        private void Awake()
        {
            view = GetComponent<RawImage>();
            player = GetComponent<VideoPlayer>();

            // 재생 시점은 코드가 정한다(Prepare 완료 후). 인스펙터 값에 기대지 않도록 여기서 고정한다.
            player.playOnAwake = false;
            player.isLooping = loop;
            player.skipOnDrop = true;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.audioOutputMode = playAudio ? VideoAudioOutputMode.Direct : VideoAudioOutputMode.None;
            player.prepareCompleted += OnPrepared;

            view.enabled = false;
        }

        private void OnDisable()
        {
            // 패널이 꺼지면 영상도 필요 없다. 꺼진 VideoPlayer는 Prepare도 못 하므로 요청도 함께 끊는다.
            Stop();
        }

        private void OnDestroy()
        {
            if (player != null) player.prepareCompleted -= OnPrepared;
            if (handle.IsValid()) Addressables.Release(handle);
            if (runtimeTexture != null)
            {
                runtimeTexture.Release();
                Destroy(runtimeTexture);
            }
        }

        /// <summary>
        /// 주소의 영상을 불러 재생한다. 준비가 끝날 때까지 RawImage는 숨겨 둔다
        /// (clip을 넣자마자 보이면 첫 프레임 디코딩 전이라 검은 화면·이전 영상 잔상이 비친다).
        /// 오브젝트가 활성 상태일 때 호출해야 한다.
        /// </summary>
        /// <param name="address">VideoClip 어드레서블 주소</param>
        /// <param name="onUnavailable">주소가 비었거나 미등록·로드 실패일 때 호출. 호출자가 대체 이미지를 띄운다</param>
        public async void Play(string address, Action onUnavailable = null)
        {
            Stop();
            int id = requestId;

            if (string.IsNullOrEmpty(address))
            {
                onUnavailable?.Invoke();
                return;
            }

            // 없는 키로 LoadAssetAsync를 부르면 콘솔에 예외 로그가 남으므로 위치부터 확인한다(ItemIconLoader와 동일).
            // 외부 에셋을 아직 동기화하지 않은 PC에서도 조용히 대체 이미지로 넘어가게 하기 위함이다.
            var locHandle = Addressables.LoadResourceLocationsAsync(address, typeof(VideoClip));
            await locHandle.Task;
            bool exists = locHandle.Status == AsyncOperationStatus.Succeeded
                          && locHandle.Result != null && locHandle.Result.Count > 0;
            Addressables.Release(locHandle);

            if (this == null || id != requestId) return;   // 기다리는 사이 다른 요청/정지가 들어옴
            if (!exists)
            {
                onUnavailable?.Invoke();
                return;
            }

            var loaded = Addressables.LoadAssetAsync<VideoClip>(address);
            await loaded.Task;

            // 늦게 도착한 옛 요청은 쓰지 않고 바로 내린다 — 여기서 Release를 빼먹으면 선택할 때마다 클립이 샌다.
            if (this == null || id != requestId || !isActiveAndEnabled)
            {
                Addressables.Release(loaded);
                return;
            }
            if (loaded.Status != AsyncOperationStatus.Succeeded || loaded.Result == null)
            {
                Addressables.Release(loaded);
                onUnavailable?.Invoke();
                return;
            }

            handle = loaded;
            player.clip = loaded.Result;
            EnsureTexture((int)loaded.Result.width, (int)loaded.Result.height);
            player.Prepare();
        }

        /// <summary>
        /// 재생을 멈추고 클립을 메모리에서 내린다. 진행 중인 로드 요청도 무효화한다.
        /// 정지 → clip 분리 → Release 순서를 지킨다(재생 중인 클립을 먼저 Release하면 해제된 에셋을 붙든 채 남는다).
        /// </summary>
        public void Stop()
        {
            requestId++;
            if (view != null) view.enabled = false;

            if (player != null)
            {
                player.Stop();
                player.clip = null;
                ClearTexture(player.targetTexture);
            }

            if (handle.IsValid()) Addressables.Release(handle);
            handle = default;
        }

        // Prepare가 끝나 첫 프레임을 낼 수 있을 때. 이 시점에야 재생하고 화면에 드러낸다.
        private void OnPrepared(VideoPlayer source)
        {
            // Stop/교체로 이미 내려간 클립의 늦은 콜백이면 무시한다.
            if (!handle.IsValid() || source.clip != handle.Result) return;

            source.Play();
            view.enabled = true;
        }

        // 인스펙터에 RT가 없으면 클립 해상도로 만든다. 같은 크기면 재사용해 교체마다 새로 만들지 않는다.
        private void EnsureTexture(int width, int height)
        {
            if (width <= 0 || height <= 0) return;

            if (player.targetTexture != null && player.targetTexture != runtimeTexture)
            {
                view.texture = player.targetTexture;   // 인스펙터 지정 RT 우선
                return;
            }

            if (runtimeTexture != null && (runtimeTexture.width != width || runtimeTexture.height != height))
            {
                runtimeTexture.Release();
                Destroy(runtimeTexture);
                runtimeTexture = null;
            }

            if (runtimeTexture == null)
            {
                runtimeTexture = new RenderTexture(width, height, 0) { name = $"{name}_VideoRT" };
                runtimeTexture.Create();
            }

            player.targetTexture = runtimeTexture;
            view.texture = runtimeTexture;
        }

        // RT에 남은 이전 영상의 마지막 프레임을 지운다(다음 영상 첫 프레임 전에 잔상이 비치는 것 방지).
        private static void ClearTexture(RenderTexture rt)
        {
            if (rt == null) return;
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = prev;
        }
    }
}
