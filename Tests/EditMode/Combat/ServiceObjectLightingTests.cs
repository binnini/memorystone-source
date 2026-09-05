using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Map.Unity;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 서비스 오브젝트(잡화점·캠핑카) 조명의 저작 계약(2026-09-05 사용자 요구:
    /// "캠핑카와 잡화점에 라이팅 추가. 그런데 다른 것처럼 청녹색이 아니라 따뜻한 주황색 컬러로").
    ///
    /// <para>🔴 종전에는 두 프리팹에 <b>조명이 하나도 없었다</b> — 건물(청녹 #5DC787)·보상뽑기(분홍)·
    /// 기억결(주황)은 저마다 <c>PropLight</c>를 들고 있는데 서비스 둘만 비어 있어, 밤 대역에서
    /// 「여기 뭔가 있다」는 신호가 아예 없었다.</para>
    ///
    /// <para>🔑 <b>수치가 아니라 온도를 잰다.</b> 밝기·반경은 아트 디렉션 손잡이라 바뀔 때마다 깨지면
    /// 안 되고, 사용자가 확정한 것은 「청녹이 아니라 따뜻한 색」이라는 축이다. 그래서 계약은
    /// 「조명이 있다 + 붉은 성분이 가장 크고 푸른 성분이 가장 작다」 하나다 —
    /// 누가 색을 건물 청녹으로 되돌리면 정확히 여기서 걸린다.</para>
    /// </summary>
    [Category("ShippingData")]
    public sealed class ServiceObjectLightingTests
    {
        // 서비스 오브젝트 프리팹(카탈로그가 참조하는 것과 같은 에셋).
        private const string CamperVanGuid = "e146de8de1bca431aac17b01004e44a0";
        private const string PopupShopGuid = "3f6ede863aaa14616946ec2f037050de";

        [TestCase(CamperVanGuid, "캠핑카")]
        [TestCase(PopupShopGuid, "잡화점")]
        public void ServiceObjectsCarryAWarmPropLight(string guid, string label)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            Assert.That(path, Is.Not.Empty, $"{label} 프리팹을 못 찾았다 — GUID가 바뀌었는지 확인할 것.");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);

            var propLights = prefab.GetComponentsInChildren<PropLight>(includeInactive: true);
            Assert.That(propLights, Is.Not.Empty,
                $"{label}에 PropLight가 없다 — 밤 대역 조명 배관(MapObjectVisualRegistry)이 이 마커로 조명을 찾는다.");

            foreach (var propLight in propLights)
            {
                var light = propLight.GetComponent<Light>();
                Assert.That(light, Is.Not.Null, $"{label}: PropLight 마커에 Light가 없다.");
                Assert.That(light.intensity, Is.GreaterThan(0f), $"{label}: 꺼진 조명은 없는 것과 같다.");
                Assert.That(light.color.r, Is.GreaterThan(light.color.g),
                    $"{label}: 따뜻한 색이어야 한다(붉은 성분이 가장 크다) — 지금 색은 {light.color}.");
                Assert.That(light.color.g, Is.GreaterThan(light.color.b),
                    $"{label}: 따뜻한 색이어야 한다(푸른 성분이 가장 작다) — 건물의 청녹으로 되돌아가면 여기서 걸린다.");
            }
        }
    }
}
