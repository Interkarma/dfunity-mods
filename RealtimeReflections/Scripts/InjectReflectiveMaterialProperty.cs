﻿//RealtimeReflections for Daggerfall-Unity
//http://www.reddit.com/r/dftfu
//http://www.dfworkshop.net/
//Author: Michael Rauter (a.k.a. Nystul)
//License: MIT License (http://www.opensource.org/licenses/mit-license.php)

using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.Collections.Generic;
using System.IO;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Game.Serialization;
using IniParser;

namespace RealtimeReflections
{
    public class InjectReflectiveMaterialProperty : MonoBehaviour
    {
        private bool useDeferredReflections = true;

        // Streaming World Component
        public StreamingWorld streamingWorld;

        private UpdateReflectionTextures componentUpdateReflectionTextures = null;

        private Texture texReflectionGround = null;
        private Texture texReflectionLowerLevel = null;
        private bool playerInside = false;

        private struct InsideSpecification
        { 
            public const int

            Building = 0,
            DungeonOrCastle = 1,
            Unknown = 2; 
        };

        private int whereInside = InsideSpecification.Unknown;
        private GameObject gameObjectInterior = null;
        private GameObject gameObjectDungeon = null;

        private GameObject gameObjectReflectionPlaneGroundLevel = null;
        private GameObject gameObjectReflectionPlaneSeaLevel = null;
        private GameObject gameObjectReflectionPlaneLowerLevel = null;

        private float extraTranslationY = 0.0f;

        private GameObject gameObjectStreamingTarget = null;

        private DaggerfallUnity dfUnity;

        static string GetGameObjectPath(GameObject obj)
        {
            string path = "/" + obj.name;
            while (obj.transform.parent != null)
            {
                obj = obj.transform.parent.gameObject;
                path = "/" + obj.name + path;
            }
            return path;
        }

        void Awake()
        {
            if (!componentUpdateReflectionTextures)
                componentUpdateReflectionTextures = GameObject.Find("RealtimeReflections").GetComponent<UpdateReflectionTextures>();
            if (!componentUpdateReflectionTextures)
            {
                DaggerfallUnity.LogMessage("InjectReflectiveMaterialProperty: Could not locate UpdateReflectionTextures component.", true);
                if (Application.isEditor)
                    Debug.Break();
                else
                    Application.Quit();
            }

            StreamingWorld.OnInitWorld += InjectMaterialProperties;
            StreamingWorld.OnTeleportToCoordinates += InjectMaterialProperties;
            FloatingOrigin.OnPositionUpdate += InjectMaterialProperties;
            DaggerfallTerrain.OnInstantiateTerrain += InjectMaterialProperties;
            SaveLoadManager.OnLoad += OnLoadEvent;
        }

        void OnDestroy()
        {
            StreamingWorld.OnInitWorld -= InjectMaterialProperties;
            StreamingWorld.OnTeleportToCoordinates -= InjectMaterialProperties;
            FloatingOrigin.OnPositionUpdate -= InjectMaterialProperties;
            DaggerfallTerrain.OnInstantiateTerrain -= InjectMaterialProperties;
            SaveLoadManager.OnLoad -= OnLoadEvent;
        }

        void Start()
        {
            dfUnity = DaggerfallUnity.Instance;

            useDeferredReflections = (GameManager.Instance.MainCamera.renderingPath == RenderingPath.DeferredShading);

            if (!streamingWorld)
                streamingWorld = GameObject.Find("StreamingWorld").GetComponent<StreamingWorld>();
            if (!streamingWorld)
            {
                DaggerfallUnity.LogMessage("InjectReflectiveMaterialProperty: Missing StreamingWorld reference.", true);
                if (Application.isEditor)
                    Debug.Break();
                else
                    Application.Quit();
            }

            if (GameObject.Find("DistantTerrain") != null)
            {
                Component[] components = GameObject.Find("DistantTerrain").GetComponents(typeof(Component));
                foreach (Component component in components)
                {
                    Type type = component.GetType();
                    if (type.Name == "DistantTerrain")
                    {
                        //System.Reflection.PropertyInfo pinfo = type.GetProperty("ExtraTranslationY");
                        System.Reflection.PropertyInfo extraTranslationYPropertyInfo = type.GetProperty("ExtraTranslationY");
                        extraTranslationY = (float)extraTranslationYPropertyInfo.GetValue(component, null);
                    }
                }
            }

            //gameObjectReflectionPlaneGroundLevel = componentUpdateReflectionTextures.GameobjectReflectionPlaneGround;
            //gameObjectReflectionPlaneLowerLevel = componentUpdateReflectionTextures.GameobjectReflectionPlaneLowerLevel;
            //gameObjectReflectionPlaneSeaLevel = gameObjectReflectionPlaneLowerLevel;

            // get inactive gameobject StreamingTarget (just GameObject.Find() would fail to find inactive gameobjects)
            GameObject[] gameObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (GameObject currentGameObject in gameObjects)
            {
                string objectPathInHierarchy = GetGameObjectPath(currentGameObject);
                if (objectPathInHierarchy == "/Exterior/StreamingTarget")
                {
                    gameObjectStreamingTarget = currentGameObject;
                }
            }
        }

        void Update()
        {
            gameObjectReflectionPlaneGroundLevel = componentUpdateReflectionTextures.GameobjectReflectionPlaneGround;
            gameObjectReflectionPlaneLowerLevel = componentUpdateReflectionTextures.GameobjectReflectionPlaneLowerLevel;
            gameObjectReflectionPlaneSeaLevel = gameObjectReflectionPlaneLowerLevel;

            if (!CheckAvailabilityAndUpdateReflectionTextures())
                return;

            // mechanism implemented according to Interkarma's suggestions
            // transition: outside -> dungeon/castle/building
            if (GameManager.Instance.PlayerEnterExit.IsPlayerInside && !playerInside)
            {
                playerInside = true; // player now inside

                // do other stuff when player first inside                    
                if (GameManager.Instance.IsPlayerInsideBuilding)
                {
                    gameObjectInterior = GameObject.Find("Interior");
                    whereInside = InsideSpecification.Building;
                }
                else if ((GameManager.Instance.IsPlayerInsideDungeon) || (GameManager.Instance.IsPlayerInsideCastle))
                {
                    gameObjectDungeon = GameObject.Find("Dungeon");
                    whereInside = InsideSpecification.DungeonOrCastle;
                }

                InjectMaterialPropertiesIndoor();
            }
            // transition: dungeon/castle/building -> outside
            else if (!GameManager.Instance.PlayerEnterExit.IsPlayerInside && playerInside)
            {
                playerInside = false; // player no longer inside

                // do other stuff when player first not inside
                gameObjectInterior = null;
                gameObjectDungeon = null;
                InjectMaterialPropertiesOutdoor();
                whereInside = InsideSpecification.Unknown;
            }

            // transition: dungeon/castle -> building
            if ((GameManager.Instance.IsPlayerInsideBuilding) && (whereInside == InsideSpecification.DungeonOrCastle))
            {
                gameObjectInterior = GameObject.Find("Interior");
                gameObjectDungeon = null;
                InjectMaterialPropertiesIndoor();
                //injectIndoor = true;
                whereInside = InsideSpecification.Building;
            }
            // transition: building -> dungeon/castle
            else if (((GameManager.Instance.IsPlayerInsideDungeon) || (GameManager.Instance.IsPlayerInsideCastle)) && (whereInside == InsideSpecification.Building))
            {
                gameObjectDungeon = GameObject.Find("Dungeon");
                gameObjectInterior = null;
                InjectMaterialPropertiesIndoor();
                whereInside = InsideSpecification.DungeonOrCastle;
            }
        }

        public void OnWillRenderObject()
        {
            if (!GameManager.Instance.IsPlayerInside)
            {
                if (!gameObjectStreamingTarget)
                    return;

                foreach (Transform child in gameObjectStreamingTarget.transform)
                {
                    DaggerfallTerrain dfTerrain = child.GetComponent<DaggerfallTerrain>();
                    if (!dfTerrain)
                        continue;

                    Terrain terrain = child.GetComponent<Terrain>();
                    if (terrain)
                    {
                        if (terrain.materialTemplate)
                        {
                            if ((terrain.materialTemplate.shader.name == "Daggerfall/RealtimeReflections/TilemapWithReflections") || (terrain.materialTemplate.shader.name == "Daggerfall/RealtimeReflections/TilemapTextureArrayWithReflections"))
                            {
                                terrain.materialTemplate.SetFloat("_GroundLevelHeight", gameObjectReflectionPlaneGroundLevel.transform.position.y - extraTranslationY);
                                terrain.materialTemplate.SetFloat("_SeaLevelHeight", gameObjectReflectionPlaneSeaLevel.transform.position.y - extraTranslationY);
                            }
                        }
                    }
                }
            }
            //else if (GameManager.Instance.IsPlayerInside)
            //{
            //    Renderer[] renderers = null;
            //    // renderers must be aquired here and not in Update() because it seems that this function's execution can happen in parallel to Update() - so a concurrent conflict can occur (and does)
            //    if (gameObjectInterior != null)
            //    {
            //        renderers = gameObjectInterior.GetComponentsInChildren<Renderer>();
            //    }
            //    else if (gameObjectDungeon != null)
            //    {
            //        renderers = gameObjectDungeon.GetComponentsInChildren<Renderer>();
            //    }

            //    //Debug.Log(String.Format("renderers: {0}", renderers.Length));

            //    if (renderers != null)
            //    {
            //        foreach (Renderer r in renderers)
            //        {
            //            Material[] mats = r.sharedMaterials;
            //            foreach (Material m in mats)
            //            {
            //                //if (m.shader.name == "Daggerfall/RealtimeReflections/FloorMaterialWithReflections")
            //                {
            //                    m.SetFloat("_GroundLevelHeight", gameObjectReflectionPlaneGroundLevel.transform.position.y);
            //                    m.SetFloat("_LowerLevelHeight", gameObjectReflectionPlaneLowerLevel.transform.position.y);
            //                }
            //            }
            //            r.sharedMaterials = mats;
            //        }
            //    }
            //}
        }

        bool CheckAvailabilityAndUpdateReflectionTextures()
        {
            bool allNeededReflectionTexturesWereAlreadyPresent = true;
            if ((
                    (componentUpdateReflectionTextures.IsEnabledOutdoorGroundReflections && componentUpdateReflectionTextures.CurrentPlayerEnvironment == UpdateReflectionTextures.PlayerEnvironment.Outdoors) ||
                    (componentUpdateReflectionTextures.IsEnabledDungeonGroundReflections && componentUpdateReflectionTextures.CurrentPlayerEnvironment == UpdateReflectionTextures.PlayerEnvironment.DungeonOrCastle) ||
                    (componentUpdateReflectionTextures.IsEnabledIndoorBuildingFloorReflections && componentUpdateReflectionTextures.CurrentPlayerEnvironment == UpdateReflectionTextures.PlayerEnvironment.Building)
                ) && texReflectionGround == null)
            {
                if (gameObjectReflectionPlaneGroundLevel != null)
                    texReflectionGround = gameObjectReflectionPlaneGroundLevel.GetComponent<MirrorReflection>().m_ReflectionTexture;
                allNeededReflectionTexturesWereAlreadyPresent = false;
            }

            if ((
                    (componentUpdateReflectionTextures.IsEnabledOutdoorSeaReflections && componentUpdateReflectionTextures.CurrentPlayerEnvironment == UpdateReflectionTextures.PlayerEnvironment.Outdoors) ||
                    (componentUpdateReflectionTextures.IsEnabledDungeonWaterReflections && componentUpdateReflectionTextures.CurrentPlayerEnvironment == UpdateReflectionTextures.PlayerEnvironment.DungeonOrCastle) ||
                    (componentUpdateReflectionTextures.IsEnabledIndoorBuildingLowerLevelReflection && componentUpdateReflectionTextures.CurrentPlayerEnvironment == UpdateReflectionTextures.PlayerEnvironment.Building)
                ) && texReflectionLowerLevel == null)
            {
                if (gameObjectReflectionPlaneLowerLevel != null)
                    texReflectionLowerLevel = gameObjectReflectionPlaneLowerLevel.GetComponent<MirrorReflection>().m_ReflectionTexture;
                allNeededReflectionTexturesWereAlreadyPresent = false;
            }
            return allNeededReflectionTexturesWereAlreadyPresent;
        }

        void InjectMaterialPropertiesIndoor()
        {
            // mages guild 4 floors debuging worldpos: 704,337
            if ((!GameManager.Instance.IsPlayerInsideCastle && !GameManager.Instance.IsPlayerInsideDungeon) || !componentUpdateReflectionTextures.IsEnabledDungeonWaterReflections)
                return;

            Renderer[] renderers = null;
            if (gameObjectDungeon != null)
            {
                renderers = gameObjectDungeon.GetComponentsInChildren<Renderer>();
            }

            if (renderers != null)
            {
                foreach (Renderer r in renderers)
                {
                    Material[] mats = r.sharedMaterials;

                    for (int m = 0; m < mats.Length; m++)
                    {
                        if (mats[m].shader.name == "FX/DungeonWater (Basic)")
                        {
                            Material matWaterReflective = new Material(componentUpdateReflectionTextures.ShaderDungeonWaterWithReflections);
                            matWaterReflective.CopyPropertiesFromMaterial(mats[m]);
                            matWaterReflective.SetTexture("_ReflectionTex", texReflectionLowerLevel);
                            mats[m] = matWaterReflective;
                        }
                    }
                    r.sharedMaterials = mats;
                }
            }
        }

        void InjectMaterialPropertiesOutdoor()
        {
            if (GameManager.Instance.IsPlayerInside || (!componentUpdateReflectionTextures.IsEnabledOutdoorGroundReflections && !componentUpdateReflectionTextures.IsEnabledOutdoorSeaReflections))
                return;
            
            GameObject go = GameObject.Find("StreamingTarget");
            if (!go)
            {
                return;
            }

            foreach (Transform child in go.transform)
            {
                DaggerfallTerrain dfTerrain = child.GetComponent<DaggerfallTerrain>();
                if (!dfTerrain)
                    continue;

                PlayerGPS playerGPS = GameObject.Find("PlayerAdvanced").GetComponent<PlayerGPS>();
                if (!playerGPS)
                    continue;

                //if ((dfTerrain.MapPixelX != playerGPS.CurrentMapPixel.X) || (dfTerrain.MapPixelY != playerGPS.CurrentMapPixel.Y))
                //    continue;


                Terrain terrain = child.GetComponent<Terrain>();
                
                if (terrain)
                {
                    if ((terrain.materialTemplate)) //&&(terrain.materialTemplate.shader.name != "Daggerfall/TilemapWithReflections")) // uncommenting this makes initial location (after startup, not fast travelling) not receive correct shader - don't know why - so workaround is to force injecting materialshader even for unset material (not sure why it works, but it does)
                    {
                        if ((SystemInfo.supports2DArrayTextures) && DaggerfallUnity.Settings.EnableTextureArrays)
                        {
                            if (terrain.materialTemplate.HasProperty("_TileTexArr") &&
                                terrain.materialTemplate.HasProperty("_TileNormalMapTexArr") &&
                                terrain.materialTemplate.HasProperty("_TileMetallicGlossMapTexArr") &&
                                terrain.materialTemplate.HasProperty("_TilemapTex") &&
                                terrain.materialTemplate.HasProperty("_TilemapDim"))
                            {
                                Texture tileTextureArray = terrain.materialTemplate.GetTexture("_TileTexArr");
                                Texture tileNormalMapTextureArray = terrain.materialTemplate.GetTexture("_TileNormalMapTexArr");
                                Texture tileMetallicGlossMapTextureArray = terrain.materialTemplate.GetTexture("_TileMetallicGlossMapTexArr");

                                Texture tileMapTexture = terrain.materialTemplate.GetTexture("_TilemapTex");
                                int tileMapDim = terrain.materialTemplate.GetInt("_TilemapDim");

                                Material newMat = new Material(componentUpdateReflectionTextures.ShaderTilemapTextureArrayWithReflections);

                                newMat.SetTexture("_TileTexArr", tileTextureArray);
                                newMat.SetTexture("_TileNormalMapTexArr", tileNormalMapTextureArray);
                                if (terrain.materialTemplate.IsKeywordEnabled("_NORMALMAP"))
                                    newMat.EnableKeyword("_NORMALMAP");
                                else
                                    newMat.DisableKeyword("_NORMALMAP");
                                newMat.SetTexture("_TileMetallicGlossMapTexArr", tileMetallicGlossMapTextureArray);
                                newMat.SetTexture("_TilemapTex", tileMapTexture);
                                newMat.SetInt("_TilemapDim", tileMapDim);

                                newMat.SetTexture("_ReflectionGroundTex", texReflectionGround);

                                newMat.SetFloat("_GroundLevelHeight", gameObjectReflectionPlaneLowerLevel.transform.position.y);

                                newMat.SetTexture("_ReflectionSeaTex", texReflectionLowerLevel);

                                newMat.SetFloat("_SeaLevelHeight", gameObjectReflectionPlaneSeaLevel.transform.position.y);

                                terrain.materialTemplate = newMat;
                            }
                        }
                        else
                        {
                            if (terrain.materialTemplate.HasProperty("_TileAtlasTex") &&
                                terrain.materialTemplate.HasProperty("_TilemapTex") &&
                                terrain.materialTemplate.HasProperty("_TilemapDim"))
                            {
                                Texture tileSetTexture = terrain.materialTemplate.GetTexture("_TileAtlasTex");

                                //Texture2D tileAtlas = dfUnity.MaterialReader.TextureReader.GetTerrainTilesetTexture(402).albedoMap;
                                //System.IO.File.WriteAllBytes("./Assets/Daggerfall/RealtimeReflections/Resources/tileatlas_402.png", tileAtlas.EncodeToPNG());

                                Texture tileMapTexture = terrain.materialTemplate.GetTexture("_TilemapTex");
                                int tileMapDim = terrain.materialTemplate.GetInt("_TilemapDim");

                                Material newMat = new Material(componentUpdateReflectionTextures.ShaderTilemapWithReflections);

                                newMat.SetTexture("_TileAtlasTex", tileSetTexture);
                                newMat.SetTexture("_TilemapTex", tileMapTexture);
                                newMat.SetInt("_TilemapDim", tileMapDim);

                                newMat.SetTexture("_ReflectionGroundTex", texReflectionGround);

                                newMat.SetFloat("_GroundLevelHeight", gameObjectReflectionPlaneLowerLevel.transform.position.y);

                                newMat.SetTexture("_ReflectionSeaTex", texReflectionLowerLevel);

                                newMat.SetFloat("_SeaLevelHeight", gameObjectReflectionPlaneSeaLevel.transform.position.y);

                                WeatherManager weatherManager = GameObject.Find("WeatherManager").GetComponent<WeatherManager>();
                                if (!weatherManager.IsRaining)
                                {
                                    //Texture2D tileAtlasReflectiveTexture = Resources.Load("tileatlas_reflective") as Texture2D;
                                    Texture2D tileAtlasReflectiveTexture = componentUpdateReflectionTextures.TextureTileatlasReflective;
                                    newMat.SetTexture("_TileAtlasReflectiveTex", tileAtlasReflectiveTexture);
                                }
                                else
                                {
                                    //Texture2D tileAtlasReflectiveTexture = Resources.Load("tileatlas_reflective_raining") as Texture2D;
                                    Texture2D tileAtlasReflectiveTexture = componentUpdateReflectionTextures.TextureTileatlasReflectiveRaining;
                                    newMat.SetTexture("_TileAtlasReflectiveTex", tileAtlasReflectiveTexture);
                                }

                                terrain.materialTemplate = newMat;
                            }
                        }
                    }
                }
            }
        }

        //overloaded variant
        void InjectMaterialProperties(DaggerfallTerrain sender)
        {
            InjectMaterialProperties(-1, -1);
        }

        //overloaded variant
        void InjectMaterialProperties(DFPosition worldPos)
        {
            InjectMaterialProperties(worldPos.X, worldPos.Y);
        }

        //overloaded variant
        void InjectMaterialProperties(Vector3 offset)
        {
            InjectMaterialProperties();
        }

        //overloaded variant
        void InjectMaterialProperties()
        {
            InjectMaterialProperties(-1, -1);
        }

        void InjectMaterialProperties(int worldPosX, int worldPosY)
        {
            if (!GameManager.Instance.IsPlayerInside)
            {
                InjectMaterialPropertiesOutdoor();
            }
            else
            {
                InjectMaterialPropertiesIndoor();
            }
        }

        void OnLoadEvent(SaveData_v1 saveData)
        {
            InjectMaterialProperties();
        }
    }
}