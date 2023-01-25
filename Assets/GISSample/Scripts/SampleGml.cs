using System;
using System.Collections.Generic;
using UnityEngine;
using PLATEAU.CityGML;
using UnityEngine.Assertions;
using System.Linq;

namespace PLATEAU.Samples
{
    /// <summary>
    /// フィルターパラメータ
    /// </summary>
    public struct FilterParameter
    {
        /// <summary>
        /// 最小高さ
        /// </summary>
        public float MinHeight;

        /// <summary>
        /// 最大高さ
        /// </summary>
        public float MaxHeight;

        /// <summary>
        /// 最小LOD
        /// </summary>
        public int MinLod;

        /// <summary>
        /// 最大LOD
        /// </summary>
        public int MaxLod;
    }


    /// <summary>
    /// 色分けタイプ
    /// </summary>
    public enum ColorCodeType
    {
        /// <summary>
        /// なし
        /// </summary>
        None,

        /// <summary>
        /// 高さ
        /// </summary>
        Height,

        /// <summary>
        /// 浸水ランク
        /// </summary>
        FloodingRank,
    }


    /// <summary>
    /// 属性情報のラッパー
    /// </summary>
    public class SampleAttribute
    {
        /// <summary>
        /// キーデータ
        /// </summary>
        public struct KeyPath
        {
            /// <summary>
            /// 属性キー
            /// </summary>
            public string Key;

            /// <summary>
            /// ルートのキーから結合したキー
            /// "_"でJoinしています。
            /// </summary>
            public string Path;
        }

        /// <summary>
        /// 浸水エリア情報
        /// </summary>
        public struct FloodingAreaInfo
        {
            /// <summary>
            /// 浸水エリア名
            /// </summary>
            public string AreaName;

            /// <summary>
            /// 浸水ランク
            /// </summary>
            public int Rank;
        }

        public readonly double? MeasuredHeight;
        public readonly AttributesMap AttributesMap;
        public readonly string RawText;

        public SampleAttribute(AttributesMap attributesMap)
        {
            AttributesMap = attributesMap;
            RawText = AttributesMap.ToString();

            MeasuredHeight = AttributesMap.GetValueOrNull("bldg:measuredheight")?.AsDouble;
        }

        /// <summary>
        /// List化された属性情報を返す
        /// AttributesMap内の全ての情報をListに変換しています。
        /// </summary>
        /// <returns></returns>
        public List<KeyValuePair<KeyPath, string>> GetKeyValues()
        {
            var keyValues = new List<KeyValuePair<KeyPath, string>>();
            GetKeyValuesInner(AttributesMap, "", keyValues);

            return keyValues;
        }

        /// <summary>
        /// 浸水エリア情報を返す
        /// </summary>
        /// <returns></returns>
        public List<FloodingAreaInfo> GetFloodingAreaInfos()
        {
            var infos = new List<FloodingAreaInfo>();
            GetFloodingAreaInfosInner(AttributesMap, infos);

            return infos;
        }

        private void GetKeyValuesInner(
            AttributesMap attributesMap,
            string parentPath,
            List<KeyValuePair<KeyPath, string>> keyValues
        )
        {
            foreach (var keyValue in attributesMap)
            {
                var path = string.IsNullOrEmpty(parentPath)
                    ? keyValue.Key
                    : string.Join("_", parentPath, keyValue.Key);

                if (keyValue.Value.Type == AttributeType.AttributeSet)
                {
                    GetKeyValuesInner(keyValue.Value.AsAttrSet, path, keyValues);
                }
                else
                {
                    keyValues.Add(
                        new KeyValuePair<KeyPath, string>(
                            new KeyPath { Key = keyValue.Key, Path = path },
                            keyValue.Value.AsString
                        )
                    );
                }
            }
        }

        private void GetFloodingAreaInfosInner(AttributesMap attributesMap, List<FloodingAreaInfo> infos)
        {
            foreach (var keyValue in attributesMap)
            {
                if (keyValue.Value.Type != AttributeType.AttributeSet) continue;

                // 浸水エリア情報のキー名は不定なので、
                // キー名が"浸水ランク"を含むAttrSetを浸水エリア情報のテーブルとみなします。
                var attrSet = keyValue.Value.AsAttrSet;
                if (attrSet.ContainsKey("浸水ランク"))
                {
                    var info = new FloodingAreaInfo
                    {
                        AreaName = keyValue.Key,
                        Rank = attrSet["浸水ランク"].AsInt,
                    };
                    infos.Add(info);
                }
                else
                {
                    GetFloodingAreaInfosInner(attrSet, infos);
                }
            }
        }
    }


    /// <summary>
    /// CityObjectのラッパー
    /// </summary>
    public class SampleCityObject
    {
        public readonly string Id;
        public readonly CityObject CityObject;

        /// <summary>
        /// CityObjectに対応するGameObjectリスト
        /// 配列のインデックスがLODのレベルに対応しています。
        /// </summary>
        public readonly GameObject[] LodObjects;

        public readonly SampleAttribute Attribute;

        public SampleCityObject(string id, CityObject cityObject)
        {
            Id = id;
            CityObject = cityObject;
            Attribute = new SampleAttribute(cityObject.AttributesMap);
            LodObjects = new GameObject[4];
        }

        /// <summary>
        /// フィルタリング
        /// パラメータに応じてオブジェクトを表示、非表示化します。
        /// </summary>
        /// <param name="parameter"></param>
        public void Filter(FilterParameter parameter)
        {
            if (Attribute.MeasuredHeight.HasValue)
            {
                foreach (var lod in LodObjects)
                {
                    if (lod == null) continue;
                    lod.SetActive(false);
                }

                var measuredHeight = Attribute.MeasuredHeight.Value;
                if (measuredHeight < parameter.MinHeight || measuredHeight > parameter.MaxHeight)
                {
                    return;
                }

                try
                {
                    int level = -1;
                    for (int i = 0; i < LodObjects.Length; ++i)
                    {
                        if (LodObjects[i] != null && i >= parameter.MinLod && i <= parameter.MaxLod) level = i;
                    }

                    if (level >= 0 && level <= 3)
                    {
                        LodObjects[level].SetActive(true);
                    }
                }
                catch (InvalidOperationException)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// 色分け
        /// </summary>
        /// <param name="type"></param>
        /// <param name="colorTable"></param>
        /// <param name="areaName">浸水エリア名</param>
        public void ColorCode(ColorCodeType type, Color[] colorTable, string areaName)
        {
            switch (type)
            {
                case ColorCodeType.None:
                default:
                    SetMaterialColor(Color.white);
                    break;
                case ColorCodeType.Height:
                    ColorCodeByHeight(colorTable);
                    break;
                case ColorCodeType.FloodingRank:
                    ColorCodeByFloodingRank(colorTable, areaName);
                    break;
            }
        }


        private void ColorCodeByHeight(Color[] colorTable)
        {
            Assert.AreEqual(6, colorTable.Length, "高さの色分けは6色");

            if (!Attribute.MeasuredHeight.HasValue)
            {
                SetMaterialColor(Color.white);
                return;
            }

            var height = Attribute.MeasuredHeight.Value;
            if (height <= 12)
            {
                SetMaterialColor(colorTable[0]);
            }
            else if (height > 12 && height <= 31)
            {
                SetMaterialColor(colorTable[1]);
            }
            else if (height > 31 && height <= 60)
            {
                SetMaterialColor(colorTable[2]);
            }
            else if (height > 60 && height <= 120)
            {
                SetMaterialColor(colorTable[3]);
            }
            else if (height > 120 && height <= 180)
            {
                SetMaterialColor(colorTable[4]);
            }
            else
            {
                SetMaterialColor(colorTable[5]);
            }
        }

        private void ColorCodeByFloodingRank(Color[] colorTable, string areaName)
        {
            Assert.AreEqual(4, colorTable.Length, "ランクの色分けは4色");

            var infos = Attribute.GetFloodingAreaInfos();
            var index = infos.FindIndex(info => info.AreaName == areaName);
            if (index < 0)
            {
                SetMaterialColor(Color.white);
                return;
            }

            var info = infos[index];
            switch (info.Rank)
            {
                case 1:
                    SetMaterialColor(colorTable[0]);
                    break;
                case 2:
                    SetMaterialColor(colorTable[1]);
                    break;
                case 3:
                    SetMaterialColor(colorTable[2]);
                    break;
                case 4:
                    SetMaterialColor(colorTable[3]);
                    break;
                default:
                    SetMaterialColor(Color.white);
                    break;
            }
        }

        public void SetMaterialColor(Color color)
        {
            foreach (var lod in LodObjects)
            {
                if (lod == null) continue;
                if (!lod.TryGetComponent<Renderer>(out var renderer)) continue;

                for (int i = 0; i < renderer.materials.Length; ++i)
                {
                    renderer.materials[i].color = color;
                }
            }
        }
    }


    /// <summary>
    /// CityModelのラッパー
    /// </summary>
    public class SampleGml
    {
        public readonly CityModel CityModel;
        public readonly GameObject GameObject;
        public readonly Dictionary<string, SampleCityObject> CityObjects;
        public readonly HashSet<string> FloodingAreaNames;

        public SampleGml(CityModel cityModel, GameObject gameObject)
        {
            CityModel = cityModel;
            GameObject = gameObject;
            CityObjects = new Dictionary<string, SampleCityObject>();
            FloodingAreaNames = new HashSet<string>();

            foreach (Transform lodTransform in gameObject.transform)
            {
                foreach (Transform cityObjectTransform in lodTransform)
                {
                    var id = cityObjectTransform.name;
                    if (!CityObjects.ContainsKey(id))
                    {
                        try
                        {
                            var cityObject = cityModel.GetCityObjectById(id);
                            CityObjects[id] = new SampleCityObject(id, cityObject);

                            foreach (var info in CityObjects[id].Attribute.GetFloodingAreaInfos())
                            {
                                FloodingAreaNames.Add(info.AreaName);
                            }
                        }
                        catch (KeyNotFoundException)
                        {
                            continue;
                        }
                    }

                    var level = -1;
                    if (lodTransform.name == "LOD0")
                    {
                        level = 0;
                    }
                    else if (lodTransform.name == "LOD1")
                    {
                        level = 1;
                    }
                    else if (lodTransform.name == "LOD2")
                    {
                        level = 2;
                    }
                    else if (lodTransform.name == "LOD3")
                    {
                        level = 3;
                    }

                    if (level != -1)
                    {
                        var go = cityObjectTransform.gameObject;
                        var material = cityObjectTransform.GetComponent<Renderer>()?.material;
                        CityObjects[id].LodObjects[level] = go;
                    }
                }
            }
        }

        /// <summary>
        /// フィルタリング
        /// </summary>
        /// <param name="parameter"></param>
        public void Filter(FilterParameter parameter)
        {
            foreach (var keyValue in CityObjects)
            {
                keyValue.Value.Filter(parameter);
            }
        }

        /// <summary>
        /// 色分け
        /// </summary>
        /// <param name="type">色分けタイプ</param>
        /// <param name="colorTable">色テーブル</param>
        /// <param name="areaName">浸水エリア名</param>
        public void ColorCode(ColorCodeType type, Color[] colorTable, string areaName = null)
        {
            foreach (var keyValue in CityObjects)
            {
                keyValue.Value.ColorCode(type, colorTable, areaName);
            }
        }
    }

}
