using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
// Include material common properties names
using static UnityEngine.Rendering.HighDefinition.HDMaterialProperties;
using UnityEditor.ShaderGraph.Drawing;

namespace UnityEditor.Rendering.HighDefinition
{
    /// <summary>
    /// The UI block that represents Shader Graph material properties.
    /// This UI block displays every non-hidden property inside a shader. You can also use this with non-shadergraph shaders.
    /// </summary>
    public class ShaderGraphUIBlock : MaterialUIBlock
    {
        /// <summary>ShaderGraph UI Block features.</summary>
        [Flags]
        public enum Features
        {
            /// <summary>Nothing is displayed.</summary>
            None = 0,
            /// <summary>Display the exposed properties.</summary>
            ExposedProperties = 1 << 1,
            /// <summary>Display the default exposed diffusion profile from the graph.</summary>
            DiffusionProfileAsset = 1 << 2,
            /// <summary>Display the shadow matte options.</summary>
            ShadowMatte = 1 << 5,
            /// <summary>Display all the Unlit fields.</summary>
            Unlit = ExposedProperties | ShadowMatte,
            /// <summary>Display all the fields.</summary>
            All = ~0,
        }

        internal static class Styles
        {
            public static GUIContent header { get; } = EditorGUIUtility.TrTextContent("Exposed Properties");
        }

        Features m_Features;

        /// <summary>
        /// Constructs a ShaderGraphUIBlock based on the parameters.
        /// </summary>
        /// <param name="expandableBit">Bit index used to store the foldout state.</param>
        /// <param name="features">Features enabled in the block.</param>
        public ShaderGraphUIBlock(ExpandableBit expandableBit = ExpandableBit.ShaderGraph, Features features = Features.All)
            : base(expandableBit, Styles.header)
        {
            m_Features = features;
        }

        /// <summary>
        /// Loads the material properties for the block.
        /// </summary>
        public override void LoadMaterialProperties() { }

        MaterialProperty[] oldProperties;

        bool CheckPropertyChanged(MaterialProperty[] properties)
        {
            bool propertyChanged = false;

            if (oldProperties != null)
            {
                // Check if shader was changed (new/deleted properties)
                if (properties.Length != oldProperties.Length)
                {
                    propertyChanged = true;
                }
                else
                {
                    for (int i = 0; i < properties.Length; i++)
                    {
                        if (properties[i].propertyType != oldProperties[i].propertyType)
                            propertyChanged = true;
                        if (properties[i].displayName != oldProperties[i].displayName)
                            propertyChanged = true;
                        if (properties[i].propertyFlags != oldProperties[i].propertyFlags)
                            propertyChanged = true;
                        if (properties[i].name != oldProperties[i].name)
                            propertyChanged = true;
                        if (properties[i].floatValue != oldProperties[i].floatValue)
                            propertyChanged = true;
                        if (properties[i].vectorValue != oldProperties[i].vectorValue)
                            propertyChanged = true;
                        if (properties[i].colorValue != oldProperties[i].colorValue)
                            propertyChanged = true;
                        if (properties[i].textureValue != oldProperties[i].textureValue)
                            propertyChanged = true;
                    }
                }
            }

            oldProperties = properties;

            return propertyChanged;
        }

        /// <summary>
        /// Renders the properties in the block.
        /// </summary>
        protected override void OnGUIOpen()
        {
            // Filter out properties we don't want to draw:
            if ((m_Features & Features.ExposedProperties) != 0)
                PropertiesDefaultGUI(properties);

            if ((m_Features & Features.DiffusionProfileAsset) != 0)
                DrawDiffusionProfileUI();

            if ((m_Features & Features.ShadowMatte) != 0 && materials.All(m => m.HasProperty(kShadowMatteFilter)))
                DrawShadowMatteToggle();
        }

        /// <summary>
        /// Draws the material properties.
        /// </summary>
        /// <param name="properties">List of Material Properties to draw</param>
        protected void PropertiesDefaultGUI(MaterialProperty[] properties)
        {
            ShaderGraphPropertyDrawers.DrawShaderGraphGUI(materialEditor, properties);
        }

        /// <summary>
        /// Draws the Shadow Matte settings. This is only available for Unlit materials.
        /// </summary>
        protected void DrawShadowMatteToggle()
        {
            uint exponent = 0b10000000; // 0 as exponent
            uint mantissa = 0x007FFFFF;

            float value = materials[0].GetFloat(HDMaterialProperties.kShadowMatteFilter);
            uint uValue = HDShadowUtils.Asuint(value);
            uint filter = uValue & mantissa;

            bool shadowFilterPoint = (filter & (uint)LightFeatureFlags.Punctual) != 0;
            bool shadowFilterDir = (filter & (uint)LightFeatureFlags.Directional) != 0;
            bool shadowFilterRect = (filter & (uint)LightFeatureFlags.Area) != 0;
            uint finalFlag = 0x00000000;
            finalFlag |= EditorGUILayout.Toggle("Point/Spot Shadow", shadowFilterPoint) ? (uint)LightFeatureFlags.Punctual : 0x00000000u;
            finalFlag |= EditorGUILayout.Toggle("Directional Shadow", shadowFilterDir) ? (uint)LightFeatureFlags.Directional : 0x00000000u;
            finalFlag |= EditorGUILayout.Toggle("Area Shadow", shadowFilterRect) ? (uint)LightFeatureFlags.Area : 0x00000000u;
            finalFlag &= mantissa;
            finalFlag |= exponent;

            materials[0].SetFloat(HDMaterialProperties.kShadowMatteFilter, HDShadowUtils.Asfloat(finalFlag));
        }

        /// <summary>
        /// Draw the built-in exposed Diffusion Profile when a material uses sub-surface scattering or transmission.
        /// </summary>
        protected void DrawDiffusionProfileUI()
        {
            if (DiffusionProfileMaterialUI.IsSupported(materialEditor))
                DiffusionProfileMaterialUI.OnGUI(materialEditor, FindProperty("_DiffusionProfileAsset"), FindProperty("_DiffusionProfileHash"), 0);
        }
    }
}
