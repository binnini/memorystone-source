using System.Reflection;
using Cinemachine;
using NUnit.Framework;
using SeoulPlayup.Combat.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatGameplayCameraPresenterTests
    {
        [Test]
        public void CinemachineRuntimeControlsExposeQeYawWithoutRightMouseDragPolicy()
        {
            var binderType = typeof(CinemachineCombatCameraBinder);

            Assert.That(
                binderType.GetField("enableKeyboardYaw", BindingFlags.Instance | BindingFlags.NonPublic),
                Is.Not.Null);
            Assert.That(
                binderType.GetField("enableRightMouseDragYaw", BindingFlags.Instance | BindingFlags.NonPublic),
                Is.Null);
            Assert.That(
                binderType.GetField("mouseYawDegreesPerPixel", BindingFlags.Instance | BindingFlags.NonPublic),
                Is.Null);
            Assert.That(
                typeof(MapCombatController).GetField("enableRmbYawOrbit", BindingFlags.Instance | BindingFlags.NonPublic),
                Is.Null);
            Assert.That(
                typeof(MapCombatController).GetField("orbitYawSensitivity", BindingFlags.Instance | BindingFlags.NonPublic),
                Is.Null);
        }

        [Test]
        public void CinemachineBinderConfiguresStableWorldSpaceOrbitalFollow()
        {
            var virtualCameraObject = new GameObject("Cinemachine Test Virtual Camera");
            var targetObject = new GameObject("Cinemachine Test Follow Target");
            var sourceCameraObject = new GameObject("Cinemachine Test Source Camera");
            var sourceCamera = sourceCameraObject.AddComponent<Camera>();
            var virtualCamera = virtualCameraObject.AddComponent<CinemachineVirtualCamera>();
            var binder = virtualCameraObject.AddComponent<CinemachineCombatCameraBinder>();
            try
            {
                sourceCamera.fieldOfView = 48f;
                SetPrivateField(binder, "virtualCamera", virtualCamera);
                SetPrivateField(binder, "followTarget", targetObject.transform);
                SetPrivateField(binder, "sourceCamera", sourceCamera);
                SetPrivateField(binder, "orbitDegrees", 90f);

                InvokePrivateMethod(binder, "InitializeOffsetState");
                SetPrivateField(binder, "orbitDegrees", 90f);
                InvokePrivateMethod(binder, "ConfigureVirtualCamera");

                var transposer = virtualCamera.GetCinemachineComponent<CinemachineOrbitalTransposer>();
                var composer = virtualCamera.GetCinemachineComponent<CinemachineComposer>();
                var impulseListener = virtualCamera.GetComponent<CinemachineImpulseListener>();

                Assert.That(virtualCamera.Follow, Is.SameAs(targetObject.transform));
                Assert.That(virtualCamera.LookAt, Is.SameAs(targetObject.transform));
                Assert.That(virtualCamera.m_Lens.FieldOfView, Is.EqualTo(48f).Within(0.001f));
                Assert.That(transposer, Is.Not.Null);
                Assert.That(transposer.m_BindingMode, Is.EqualTo(CinemachineTransposer.BindingMode.WorldSpace));
                Assert.That(transposer.m_FollowOffset.x, Is.EqualTo(-7f).Within(0.001f));
                Assert.That(transposer.m_FollowOffset.y, Is.EqualTo(9.5f).Within(0.001f));
                Assert.That(transposer.m_FollowOffset.z, Is.EqualTo(0f).Within(0.001f));
                Assert.That(
                    transposer.m_Heading.m_Definition,
                    Is.EqualTo(CinemachineOrbitalTransposer.Heading.HeadingDefinition.WorldForward));
                Assert.That(transposer.m_RecenterToTargetHeading.m_enabled, Is.False);
                Assert.That(transposer.m_XAxis.Value, Is.Zero);
                Assert.That(composer, Is.Not.Null);
                Assert.That(composer.m_ScreenX, Is.EqualTo(0.5f).Within(0.001f));
                Assert.That(composer.m_ScreenY, Is.EqualTo(0.5f).Within(0.001f));
                Assert.That(composer.m_DeadZoneWidth, Is.Zero);
                Assert.That(composer.m_DeadZoneHeight, Is.Zero);
                Assert.That(impulseListener, Is.Not.Null);
                Assert.That(impulseListener.m_ApplyAfter, Is.EqualTo(CinemachineCore.Stage.Noise));
                Assert.That(impulseListener.m_ChannelMask, Is.EqualTo(1));
                Assert.That(impulseListener.m_Gain, Is.EqualTo(1f).Within(0.001f));
                Assert.That(impulseListener.m_UseCameraSpace, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(virtualCameraObject);
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(sourceCameraObject);
            }
        }

        [Test]
        public void CinemachineBinderUsesProfileTuningWhenAssigned()
        {
            var virtualCameraObject = new GameObject("Cinemachine Profile Virtual Camera");
            var targetObject = new GameObject("Cinemachine Profile Follow Target");
            var virtualCamera = virtualCameraObject.AddComponent<CinemachineVirtualCamera>();
            var binder = virtualCameraObject.AddComponent<CinemachineCombatCameraBinder>();
            var profile = ScriptableObject.CreateInstance<CombatCinemachineCameraProfile>();
            try
            {
                SetPrivateField(profile, "followOffset", new Vector3(0f, 4f, -8f));
                SetPrivateField(profile, "damping", 0.2f);
                SetPrivateField(profile, "screenX", 0.45f);
                SetPrivateField(profile, "screenY", 0.6f);
                SetPrivateField(profile, "deadZoneWidth", 0.1f);
                SetPrivateField(profile, "deadZoneHeight", 0.2f);
                SetPrivateField(profile, "minZoomDistance", 3f);
                SetPrivateField(profile, "maxZoomDistance", 12f);

                SetPrivateField(binder, "virtualCamera", virtualCamera);
                SetPrivateField(binder, "followTarget", targetObject.transform);
                SetPrivateField(binder, "profile", profile);

                InvokePrivateMethod(binder, "InitializeOffsetState");
                InvokePrivateMethod(binder, "ConfigureVirtualCamera");

                var transposer = virtualCamera.GetCinemachineComponent<CinemachineOrbitalTransposer>();
                var composer = virtualCamera.GetCinemachineComponent<CinemachineComposer>();

                Assert.That(binder.Profile, Is.SameAs(profile));
                Assert.That(transposer, Is.Not.Null);
                Assert.That(transposer.m_FollowOffset.x, Is.EqualTo(0f).Within(0.001f));
                Assert.That(transposer.m_FollowOffset.y, Is.EqualTo(4f).Within(0.001f));
                Assert.That(transposer.m_FollowOffset.z, Is.EqualTo(-8f).Within(0.001f));
                Assert.That(transposer.m_XDamping, Is.EqualTo(0.2f).Within(0.001f));
                Assert.That(composer, Is.Not.Null);
                Assert.That(composer.m_ScreenX, Is.EqualTo(0.45f).Within(0.001f));
                Assert.That(composer.m_ScreenY, Is.EqualTo(0.6f).Within(0.001f));
                Assert.That(composer.m_DeadZoneWidth, Is.EqualTo(0.1f).Within(0.001f));
                Assert.That(composer.m_DeadZoneHeight, Is.EqualTo(0.2f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(virtualCameraObject);
                Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void CinemachineGameplayCameraModeDoesNotOverwriteMainCameraTransform()
        {
            var controllerObject = new GameObject("Cinemachine Camera Mode Controller");
            var cameraObject = new GameObject("Cinemachine Camera Mode Camera");
            var camera = cameraObject.AddComponent<Camera>();
            var controller = controllerObject.AddComponent<MapCombatController>();
            try
            {
                var originalPosition = new Vector3(3f, 4f, 5f);
                var originalRotation = Quaternion.Euler(10f, 20f, 0f);
                camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
                controller.ConfigureForTests(null, null, camera);

                controller.ApplyGameplayCameraSettingsForDev(
                    new Vector3(1f, 2f, 3f),
                    new Vector3(42f, 8f, 0f),
                    9f,
                    keepCenteredOnPlayer: true);

                Assert.That(camera.transform.position, Is.EqualTo(originalPosition));
                Assert.That(Quaternion.Angle(camera.transform.rotation, originalRotation), Is.LessThan(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void CinemachineCameraShakeUsesImpulseSourceWithLegacyTimingValues()
        {
            var controllerObject = new GameObject("Cinemachine Camera Shake Controller");
            var controller = controllerObject.AddComponent<MapCombatController>();
            try
            {
                InvokePrivateMethod(
                    controller,
                    "AddCinemachineCameraShake",
                    0.5f,
                    0.25f,
                    12,
                    90f);

                var source = controllerObject.GetComponent<CinemachineImpulseSource>();

                Assert.That(source, Is.Not.Null);
                Assert.That(source.m_ImpulseDefinition.m_ImpulseChannel, Is.EqualTo(1));
                Assert.That(
                    source.m_ImpulseDefinition.m_ImpulseType,
                    Is.EqualTo(CinemachineImpulseDefinition.ImpulseTypes.Uniform));
                Assert.That(
                    source.m_ImpulseDefinition.m_ImpulseShape,
                    Is.EqualTo(CinemachineImpulseDefinition.ImpulseShapes.Custom));
                Assert.That(source.m_ImpulseDefinition.m_ImpulseDuration, Is.EqualTo(0.25f).Within(0.001f));
                Assert.That(source.m_ImpulseDefinition.m_CustomImpulseShape.keys.Length, Is.EqualTo(13));
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
            }
        }

        [Test]
        public void CinemachineGameplayCameraModeDoesNotApplyCameraProfileToMainCameraTransform()
        {
            var controllerObject = new GameObject("Cinemachine Profile Mode Controller");
            var cameraObject = new GameObject("Cinemachine Profile Mode Camera");
            var camera = cameraObject.AddComponent<Camera>();
            var controller = controllerObject.AddComponent<MapCombatController>();
            var profile = ScriptableObject.CreateInstance<CombatCameraProfile>();
            try
            {
                var originalPosition = new Vector3(3f, 4f, 5f);
                var originalRotation = Quaternion.Euler(10f, 20f, 0f);
                camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
                camera.orthographic = true;

                SetPrivateField(profile, "projectionMode", CombatCameraProjectionMode.Perspective);
                SetPrivateField(profile, "fieldOfView", 50f);
                SetPrivateField(profile, "eulerAngles", new Vector3(42f, 8f, 0f));
                SetPrivateField(profile, "keepCenteredOnPlayer", true);

                controller.ConfigureForTests(null, null, camera);

                controller.ApplyCameraProfileForScene(profile);

                Assert.That(controller.CameraProfile, Is.SameAs(profile));
                Assert.That(camera.transform.position, Is.EqualTo(originalPosition));
                Assert.That(Quaternion.Angle(camera.transform.rotation, originalRotation), Is.LessThan(0.01f));
                Assert.That(camera.orthographic, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void AutoCameraOffsetUsesCameraForwardDistanceAndFramingOffset()
        {
            var controllerObject = new GameObject("Auto Camera Offset Controller");
            var controller = controllerObject.AddComponent<MapCombatController>();
            try
            {
                SetPrivateField(controller, "autoCalculateCameraPlayerOffset", true);
                SetPrivateField(controller, "cameraFollowDistance", 10f);
                SetPrivateField(controller, "cameraFramingOffset", Vector2.zero);
                SetPrivateField(controller, "cameraEulerAngles", new Vector3(45f, -30f, 0f));

                var offset = GetEffectiveCameraPlayerOffset(controller);
                var expected = -(Quaternion.Euler(controller.CameraEulerAngles) * Vector3.forward) * 10f;

                Assert.That(Vector3.Distance(offset, expected), Is.LessThan(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
            }
        }

        [Test]
        public void DevCameraSettingsDoNotOverwriteManualOffsetWhenAutoOffsetIsEnabled()
        {
            var controllerObject = new GameObject("Auto Dev Camera Settings Controller");
            var cameraObject = new GameObject("Auto Dev Camera Settings Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            var controller = controllerObject.AddComponent<MapCombatController>();
            try
            {
                var compatibilityOffset = new Vector3(4f, 5f, 6f);
                SetPrivateField(controller, "cameraPlayerOffset", compatibilityOffset);
                SetPrivateField(controller, "autoCalculateCameraPlayerOffset", true);
                controller.ConfigureForTests(null, null, camera);

                controller.ApplyGameplayCameraSettingsForDev(
                    new Vector3(1f, 2f, 3f),
                    new Vector3(45f, -30f, 0f),
                    7f,
                    keepCenteredOnPlayer: false);

                Assert.That(controller.AutoCalculateCameraPlayerOffset, Is.True);
                Assert.That(controller.CameraPlayerOffset, Is.EqualTo(compatibilityOffset));
                Assert.That(controller.CameraEulerAngles, Is.EqualTo(new Vector3(45f, -30f, 0f)));
                Assert.That(controller.CameraOrthographicSize, Is.EqualTo(7f));
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(cameraObject);
            }
        }

        private static Vector3 GetEffectiveCameraPlayerOffset(MapCombatController controller)
        {
            var property = typeof(MapCombatController).GetProperty(
                "EffectiveCameraPlayerOffset",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null);
            return (Vector3)property.GetValue(controller);
        }

        private static void SetPrivateField<T>(MapCombatController controller, string fieldName, T value)
        {
            var field = typeof(MapCombatController).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(controller, value);
        }

        private static void SetPrivateField<T>(CinemachineCombatCameraBinder binder, string fieldName, T value)
        {
            var field = typeof(CinemachineCombatCameraBinder).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(binder, value);
        }

        private static void InvokePrivateMethod(CinemachineCombatCameraBinder binder, string methodName)
        {
            var method = typeof(CinemachineCombatCameraBinder).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(binder, null);
        }

        private static void InvokePrivateMethod(MapCombatController controller, string methodName, params object[] arguments)
        {
            var method = typeof(MapCombatController).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(controller, arguments);
        }

        private static void SetPrivateField<T>(CombatCameraProfile profile, string fieldName, T value)
        {
            var field = typeof(CombatCameraProfile).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(profile, value);
        }

        private static void SetPrivateField<T>(CombatCinemachineCameraProfile profile, string fieldName, T value)
        {
            var field = typeof(CombatCinemachineCameraProfile).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(profile, value);
        }
    }
}

