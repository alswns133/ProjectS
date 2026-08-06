using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ProjectS.Items
{
    /// <summary>
    /// 아이템 아이콘 스프라이트를 어드레서블로 로드해 주소별로 캐싱하는 헬퍼.
    /// 같은 주소를 여러 슬롯이 요청해도 한 번만 로드(핸들 1개)하고 스프라이트를 공유한다 —
    /// 슬롯마다 <c>LoadAssetAsync</c>를 부르면 참조 카운트가 갈라져 해제가 꼬이기 때문이다.
    /// (DialogueManager의 초상화 로드/해제 패턴을 인벤·보상·강화 슬롯이 공유하도록 일반화한 것.)
    /// 아이콘은 여러 UI가 상시 재사용하므로 씬마다 내리지 않고, 필요 시 <see cref="ReleaseAll"/>로 일괄 해제한다.
    /// </summary>
    public static class ItemIconLoader
    {
        // 주소 → 로드 핸들. 진행 중 핸들도 담아, 동시 요청이 같은 핸들의 Task를 함께 기다리게 한다.
        private static readonly Dictionary<string, AsyncOperationHandle<Sprite>> handles = new();

        // 어드레서블에 없는 주소(아이콘 미준비 등). 매번 예외를 던지지 않게 기억해 두고 바로 null을 준다.
        private static readonly HashSet<string> missing = new();

        /// <summary>
        /// 주소로 아이콘 스프라이트를 로드한다(캐시 우선). 주소가 비었거나 로드 실패면 null을 돌려주므로
        /// 호출측(슬롯)이 아이콘을 숨기는 등 폴백을 정할 수 있다.
        /// </summary>
        /// <param name="address">스프라이트 어드레서블 주소(<see cref="ProjectS.Data.ItemData.IconAddress"/>)</param>
        /// <returns>로드된 스프라이트. 실패 시 null</returns>
        public static async Task<Sprite> LoadAsync(string address)
        {
            if (string.IsNullOrEmpty(address) || missing.Contains(address)) return null;

            if (handles.TryGetValue(address, out var cached))
            {
                if (cached.IsValid())
                {
                    if (!cached.IsDone) await cached.Task;
                    return cached.Status == AsyncOperationStatus.Succeeded ? cached.Result : null;
                }

                // 무효 핸들(플레이 세션 종료 등)은 캐시에서 버리고 다시 로드한다.
                handles.Remove(address);
            }

            // 없는 키로 LoadAssetAsync를 부르면 InvalidKeyException이 던져지고 Addressables가 콘솔에도 직접 로그한다
            // (try/catch로 잡아도 그 로그는 남는다). 그래서 먼저 위치가 등록돼 있는지 확인한다 —
            // LoadResourceLocationsAsync는 없는 키에도 예외 없이 빈 결과를 주므로 콘솔이 조용하다.
            try
            {
                var locHandle = Addressables.LoadResourceLocationsAsync(address, typeof(Sprite));
                await locHandle.Task;

                bool exists = locHandle.Status == AsyncOperationStatus.Succeeded
                              && locHandle.Result != null && locHandle.Result.Count > 0;
                Addressables.Release(locHandle);

                if (!exists)
                {
                    missing.Add(address);   // 미등록 주소 기억 → 다음부터 바로 null
                    return null;
                }

                var handle = Addressables.LoadAssetAsync<Sprite>(address);
                handles[address] = handle;
                await handle.Task;

                if (handle.Status == AsyncOperationStatus.Succeeded) return handle.Result;

                handles.Remove(address);
                missing.Add(address);
                return null;
            }
            catch (Exception)
            {
                handles.Remove(address);
                missing.Add(address);
                return null;
            }
        }

        /// <summary>로드해둔 모든 아이콘 핸들을 해제한다. 아이콘 메모리를 정리할 경계에서 호출한다.</summary>
        public static void ReleaseAll()
        {
            foreach (var handle in handles.Values)
            {
                if (handle.IsValid()) Addressables.Release(handle);
            }
            handles.Clear();
        }

        // 플레이 모드 리로드 후에도 남을 수 있는 static 캐시를 초기화한다(이전 세션의 무효 핸들·미등록 기록 제거).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            handles.Clear();
            missing.Clear();
        }
    }
}
