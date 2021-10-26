//RealtimeReflections for Daggerfall-Unity
//http://www.reddit.com/r/dftfu
//http://www.dfworkshop.net/
//Author: Michael Rauter (a.k.a. Nystul)
//License: MIT License (http://www.opensource.org/licenses/mit-license.php)

// This script is derived from MirrorReflection4 script

using UnityEngine;
using UnityEngine.PostProcessing;
using System.Collections;

using DaggerfallWorkshop.Game;

namespace RealtimeReflections
{
    [ExecuteInEditMode] // Make mirror live-update even when not in play mode
    public class MirrorReflection : MonoBehaviour
    {
	    public bool m_DisablePixelLights = false;
	    public int m_TextureWidth = 256;
        public int m_TextureHeight = 256;
        public float m_ClipPlaneOffset = 0.05f; //0.0f; //0.07f;
 
	    public LayerMask m_ReflectLayers = -1;
        //public float[] layerCullDistances = new float[32];


        private GameObject goMirrorReflection;
        private Hashtable m_ReflectionCameras = new Hashtable(); // Camera -> Camera table
 
	    public RenderTexture m_ReflectionTexture = null;
	    private int m_OldReflectionTextureWidth = 0;
        private int m_OldReflectionTextureHeight = 0;

        private static bool s_InsideRendering = false;

        private Camera cameraToUse = null;

        // use same post processing profile as main camera
        private PostProcessingBehaviour postProcessingBehaviour;

        public struct EnvironmentSetting
        {
            public const int

            IndoorSetting = 0,
            OutdoorSetting = 1;        
        }
        
        private int currentBackgroundSettings = EnvironmentSetting.IndoorSetting;
        public int CurrentBackgroundSettings
        {
            get
            {
                return currentBackgroundSettings;
            }
            set { currentBackgroundSettings = value; }
        }

        void Start()
        {
            int layerWater = LayerMask.NameToLayer("Water");
            int layerUI = LayerMask.NameToLayer("UI");
            int layerSkyLayer = LayerMask.NameToLayer("SkyLayer");
            int layerAutomap = LayerMask.NameToLayer("Automap");
            int layerBankPurchase = LayerMask.NameToLayer("BankPurchase");

            //m_ReflectLayers = ~(1 << 4) & ~(1 << 5) & LayerMask.NameToLayer("Everything");
            m_ReflectLayers = ~(1 << layerWater) & ~(1 << layerUI) & ~(1 << layerSkyLayer) & ~(1 << layerAutomap) & ~(1 << layerBankPurchase) & LayerMask.NameToLayer("Everything");
            GameObject stackedCameraGameObject = GameObject.Find("stackedCamera");
            if (stackedCameraGameObject != null)
            {
                cameraToUse = stackedCameraGameObject.GetComponent<Camera>(); // when stacked camera is present use it to prevent reflection of near terrain in stackedCamera clip range distance not being updated
            }
            if (!cameraToUse)  // if stacked camera was not found us main camera
            {
                cameraToUse = Camera.main;
            }
            postProcessingBehaviour = Camera.main.GetComponent<PostProcessingBehaviour>();
        }

	    // This is called when it's known that the object will be rendered by some
	    // camera. We render reflections and do other updates here.
	    // Because the script executes in edit mode, reflections for the scene view
	    // camera will just work!
	    public void OnWillRenderObject()
        //void Update()
	    {
		    var rend = GetComponent<Renderer>();
		    if (!enabled || !rend || !rend.sharedMaterial) // || !rend.enabled)
			    return;
 
		    Camera cam = Camera.current;
		    if( !cam )
			    return;
            
            if (cam != cameraToUse) // skip every camera that is not the intended camera to use for rendering the mirrored scene
                return;

            // Safeguard from recursive reflections.        
		    if( s_InsideRendering )
			    return;
		    s_InsideRendering = true;
 
		    Camera reflectionCamera;
		    CreateMirrorObjects( cam, out reflectionCamera );
 
		    // find out the reflection plane: position and normal in world space
		    Vector3 pos = transform.position;
		    Vector3 normal = transform.up;
 
		    // Optionally disable pixel lights for reflection
		    int oldPixelLightCount = QualitySettings.pixelLightCount;
		    if( m_DisablePixelLights )
			    QualitySettings.pixelLightCount = 0;
 
		    UpdateCameraModes( cam, reflectionCamera );
 
		    // Render reflection
		    // Reflect camera around reflection plane
		    float d = -Vector3.Dot (normal, pos) - m_ClipPlaneOffset;
		    Vector4 reflectionPlane = new Vector4 (normal.x, normal.y, normal.z, d);
 
		    Matrix4x4 reflection = Matrix4x4.zero;
		    CalculateReflectionMatrix (ref reflection, reflectionPlane);
		    Vector3 oldpos = cam.transform.position;
		    Vector3 newpos = reflection.MultiplyPoint( oldpos );
		    reflectionCamera.worldToCameraMatrix = cam.worldToCameraMatrix * reflection;
 
		    // Setup oblique projection matrix so that near plane is our reflection
		    // plane. This way we clip everything below/above it for free.
		    Vector4 clipPlane = CameraSpacePlane( reflectionCamera, pos, normal, 1.0f );
            //Matrix4x4 projection = cam.projectionMatrix; // maybe outdoor we can get away (update: no we can't how it seems...) without oblique projection matrix which won't break fog because unity won't switch to forward rendering (see https://docs.unity3d.com/Manual/ObliqueFrustum.html)
            //if (GameManager.Instance.IsPlayerInside)
            //  projection = cam.CalculateObliqueMatrix(clipPlane); // important for correct indoor/dungeon reflections on upper building levels
            Matrix4x4 projection = cam.CalculateObliqueMatrix(clipPlane); // always calculate oblique projection matrix - otherwise problems when distant terrain is enabled and terrain flats are not reflected

            reflectionCamera.projectionMatrix = projection; // do not set oblique projection matrix since it will fuck up fog in reflections - disabling this step seems to do not any harm ;)

            reflectionCamera.cullingMask = m_ReflectLayers.value; // never render water layer
            //reflectionCamera.layerCullDistances = layerCullDistances;
		    reflectionCamera.targetTexture = m_ReflectionTexture;

            UnityEngine.Rendering.ShadowCastingMode oldShadowCastingMode = rend.shadowCastingMode;
            bool oldReceiverShadows = rend.receiveShadows;
            // next 2 lines are important for making shadows work correctly - otherwise shadows will be broken
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;

            GL.invertCulling = true;
		    reflectionCamera.transform.position = newpos;
		    Vector3 euler = cam.transform.eulerAngles;
		    reflectionCamera.transform.eulerAngles = new Vector3(0, euler.y, euler.z);
		    reflectionCamera.Render();
		    reflectionCamera.transform.position = oldpos;
            GL.invertCulling = false;            
		    // Restore pixel light count
		    if( m_DisablePixelLights )
			    QualitySettings.pixelLightCount = oldPixelLightCount;
 
		    s_InsideRendering = false;

            rend.shadowCastingMode = oldShadowCastingMode;
            rend.receiveShadows = oldReceiverShadows;
            
	    }
 
 
	    // Cleanup all the objects we possibly have created
	    void OnDisable()
	    {
		    if( m_ReflectionTexture ) {
			    Destroy( m_ReflectionTexture );
			    m_ReflectionTexture = null;
		    }

            Destroy(goMirrorReflection);
            //foreach( DictionaryEntry kvp in m_ReflectionCameras )
            //    DestroyImmediate( ((Camera)kvp.Value).gameObject );
            m_ReflectionCameras.Clear();
	    }
 
 
	    private void UpdateCameraModes( Camera src, Camera dest )
	    {
		    if( dest == null )
			    return;
            // set camera to clear the same way as current camera            
            CameraClearFlags clearFlags = CameraClearFlags.Skybox;
            if (currentBackgroundSettings == EnvironmentSetting.IndoorSetting)
            {
                clearFlags = CameraClearFlags.Color;
                dest.backgroundColor = Color.black;
                dest.clearFlags = clearFlags;
            }
            else if (currentBackgroundSettings == EnvironmentSetting.OutdoorSetting)
            {
                clearFlags = CameraClearFlags.Skybox;
                dest.backgroundColor = src.backgroundColor;
                dest.clearFlags = clearFlags;
            }
            if ( clearFlags == CameraClearFlags.Skybox )
		    {
                Skybox sky = src.GetComponent(typeof(Skybox)) as Skybox;
			    Skybox mysky = dest.GetComponent(typeof(Skybox)) as Skybox;
			    if( !sky || !sky.material )
			    {
				    mysky.enabled = false;
			    }
			    else
			    {
				    mysky.enabled = true;
				    mysky.material = sky.material;
			    }
		    }
		    // update other values to match current camera.
		    // even if we are supplying custom camera&projection matrices,
		    // some of values are used elsewhere (e.g. skybox uses far plane)

            // get near clipping plane from main camera
            dest.renderingPath = src.renderingPath;

            dest.farClipPlane = src.farClipPlane;

            if (currentBackgroundSettings == EnvironmentSetting.IndoorSetting)
            {
                dest.farClipPlane = 1000.0f;
            }

            dest.nearClipPlane = 0.03f; //src.nearClipPlane;
		    dest.orthographic = src.orthographic;
		    dest.fieldOfView = src.fieldOfView;
		    dest.aspect = src.aspect;
		    dest.orthographicSize = src.orthographicSize;

            //update fog settings (post processing profile)
            PostProcessingBehaviour reflectionCamPostProcessingBehaviour = dest.gameObject.GetComponent<PostProcessingBehaviour>();
            if (reflectionCamPostProcessingBehaviour)
                reflectionCamPostProcessingBehaviour.profile = postProcessingBehaviour.profile;
        }
 
	    // On-demand create any objects we need
	    private void CreateMirrorObjects( Camera currentCamera, out Camera reflectionCamera )
	    {
		    reflectionCamera = null;
 
		    // Reflection render texture
		    if( !m_ReflectionTexture || m_OldReflectionTextureWidth != m_TextureWidth || m_OldReflectionTextureHeight != m_TextureHeight)
		    {
			    if( m_ReflectionTexture )
				    Destroy( m_ReflectionTexture );
                m_ReflectionTexture = new RenderTexture(m_TextureWidth, m_TextureHeight, 16); //, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);

			    m_ReflectionTexture.name = "__MirrorReflection" + GetInstanceID();
			    m_ReflectionTexture.isPowerOfTwo = true;

                //m_ReflectionTexture.generateMips = true;
                m_ReflectionTexture.useMipMap = true;
                m_ReflectionTexture.wrapMode = TextureWrapMode.Clamp;
                m_ReflectionTexture.filterMode = FilterMode.Bilinear;

                //m_ReflectionTexture.hideFlags = HideFlags.DontSave;
                m_OldReflectionTextureWidth = m_TextureWidth;
                m_OldReflectionTextureHeight = m_TextureHeight;
            }
 
		    // Camera for reflection
		    reflectionCamera = m_ReflectionCameras[currentCamera] as Camera;
		    if( !reflectionCamera ) // catch both not-in-dictionary and in-dictionary-but-deleted-GO
		    {
			    goMirrorReflection = new GameObject( "Mirror Refl Camera id" + GetInstanceID() + " for " + currentCamera.GetInstanceID(), typeof(Camera), typeof(Skybox) );
			    reflectionCamera = goMirrorReflection.GetComponent<Camera>();
                // reflectionCamera.CopyFrom(currentCamera); //breaks outdoor reflections - TODO: investigate which settings causes the issue
                reflectionCamera.allowMSAA = false; // prevent warning in camera component
                reflectionCamera.enabled = false;
                reflectionCamera.transform.position = currentCamera.transform.position;
                reflectionCamera.transform.rotation = currentCamera.transform.rotation;
			    reflectionCamera.gameObject.AddComponent<FlareLayer>();
			    //go.hideFlags = HideFlags.HideAndDontSave;
			    m_ReflectionCameras[currentCamera] = reflectionCamera;

                if (currentBackgroundSettings == EnvironmentSetting.OutdoorSetting)
                {
                    if (postProcessingBehaviour != null)
                    {
                        PostProcessingBehaviour reflectionCamPostProcessingBehaviour = goMirrorReflection.AddComponent<PostProcessingBehaviour>();
                        reflectionCamPostProcessingBehaviour.profile = postProcessingBehaviour.profile;
                    }
                }

                goMirrorReflection.transform.SetParent(GameObject.Find("RealtimeReflections").transform);
		    }
	    }
 
	    // Extended sign: returns -1, 0 or 1 based on sign of a
	    private static float sgn(float a)
	    {
		    if (a > 0.0f) return 1.0f;
		    if (a < 0.0f) return -1.0f;
		    return 0.0f;
	    }
 
	    // Given position/normal of the plane, calculates plane in camera space.
	    private Vector4 CameraSpacePlane (Camera cam, Vector3 pos, Vector3 normal, float sideSign)
	    {
		    Vector3 offsetPos = pos + normal * m_ClipPlaneOffset;
		    Matrix4x4 m = cam.worldToCameraMatrix;
		    Vector3 cpos = m.MultiplyPoint( offsetPos );
		    Vector3 cnormal = m.MultiplyVector( normal ).normalized * sideSign;
		    return new Vector4( cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos,cnormal) );
	    }
 
	    // Calculates reflection matrix around the given plane
	    private static void CalculateReflectionMatrix (ref Matrix4x4 reflectionMat, Vector4 plane)
	    {
		    reflectionMat.m00 = (1F - 2F*plane[0]*plane[0]);
		    reflectionMat.m01 = (   - 2F*plane[0]*plane[1]);
		    reflectionMat.m02 = (   - 2F*plane[0]*plane[2]);
		    reflectionMat.m03 = (   - 2F*plane[3]*plane[0]);
 
		    reflectionMat.m10 = (   - 2F*plane[1]*plane[0]);
		    reflectionMat.m11 = (1F - 2F*plane[1]*plane[1]);
		    reflectionMat.m12 = (   - 2F*plane[1]*plane[2]);
		    reflectionMat.m13 = (   - 2F*plane[3]*plane[1]);
 
		    reflectionMat.m20 = (   - 2F*plane[2]*plane[0]);
		    reflectionMat.m21 = (   - 2F*plane[2]*plane[1]);
		    reflectionMat.m22 = (1F - 2F*plane[2]*plane[2]);
		    reflectionMat.m23 = (   - 2F*plane[3]*plane[2]);
 
		    reflectionMat.m30 = 0F;
		    reflectionMat.m31 = 0F;
		    reflectionMat.m32 = 0F;
		    reflectionMat.m33 = 1F;
	    }
    }
}