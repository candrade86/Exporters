﻿using Autodesk.Maya.OpenMaya;
using BabylonExport.Entities;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Maya2Babylon
{
    partial class BabylonExporter
    {
        /// <summary>
        /// List of simple materials
        /// </summary>
        List<MFnDependencyNode> referencedMaterials = new List<MFnDependencyNode>();

        /// <summary>
        /// List of sub materials binded to each multimaterial
        /// </summary>
        readonly Dictionary<string, List<MFnDependencyNode>> multiMaterials = new Dictionary<string, List<MFnDependencyNode>>();

        private void ExportMultiMaterial(string uuidMultiMaterial, List<MFnDependencyNode> materials, BabylonScene babylonScene)
        {
            var babylonMultimaterial = new BabylonMultiMaterial { id = uuidMultiMaterial };

            // Name
            string nameConcatenated = "";
            bool isFirstTime = true;
            List<MFnDependencyNode> materialsSorted = new List<MFnDependencyNode>(materials);
            materialsSorted.Sort(new MFnDependencyNodeComparer());
            foreach (MFnDependencyNode material in materialsSorted)
            {
                if (!isFirstTime)
                {
                    nameConcatenated += "_";
                }
                isFirstTime = false;

                nameConcatenated += material.name;
            }
            babylonMultimaterial.name = nameConcatenated;

            // Materials
            var uuids = new List<string>();
            foreach (MFnDependencyNode subMat in materials)
            {
                string uuidSubMat = subMat.uuid().asString();
                uuids.Add(uuidSubMat);

                if (!referencedMaterials.Contains(subMat, new MFnDependencyNodeEqualityComparer()))
                {
                    // Export sub material
                    referencedMaterials.Add(subMat);
                    ExportMaterial(subMat, babylonScene);
                }
            }
            babylonMultimaterial.materials = uuids.ToArray();

            babylonScene.MultiMaterialsList.Add(babylonMultimaterial);
        }

        private void ExportMaterial(MFnDependencyNode materialDependencyNode, BabylonScene babylonScene)
        {
            MObject materialObject = materialDependencyNode.objectProperty;
            var name = materialDependencyNode.name;
            var id = materialDependencyNode.uuid().asString();

            RaiseMessage(name, 1);

            RaiseVerbose("materialObject.hasFn(MFn.Type.kBlinn)=" + materialObject.hasFn(MFn.Type.kBlinn), 2);
            RaiseVerbose("materialObject.hasFn(MFn.Type.kPhong)=" + materialObject.hasFn(MFn.Type.kPhong), 2);
            RaiseVerbose("materialObject.hasFn(MFn.Type.kPhongExplorer)=" + materialObject.hasFn(MFn.Type.kPhongExplorer), 2);

            Print(materialDependencyNode, 2, "Print ExportMaterial materialDependencyNode");

            // Standard material
            if (materialObject.hasFn(MFn.Type.kLambert))
            {
                if (materialObject.hasFn(MFn.Type.kBlinn))
                {
                    RaiseMessage("Blinn shader", 2);
                }
                else if (materialObject.hasFn(MFn.Type.kPhong))
                {
                    RaiseMessage("Phong shader", 2);
                }
                else if (materialObject.hasFn(MFn.Type.kPhongExplorer))
                {
                    RaiseMessage("Phong E shader", 2);
                }
                else
                {
                    RaiseMessage("Lambert shader", 2);
                }

                var lambertShader = new MFnLambertShader(materialObject);

                RaiseVerbose("typeId=" + lambertShader.typeId, 2);
                RaiseVerbose("typeName=" + lambertShader.typeName, 2);
                RaiseVerbose("color=" + lambertShader.color.toString(), 2);
                RaiseVerbose("transparency=" + lambertShader.transparency.toString(), 2);
                RaiseVerbose("ambientColor=" + lambertShader.ambientColor.toString(), 2);
                RaiseVerbose("incandescence=" + lambertShader.incandescence.toString(), 2);
                RaiseVerbose("diffuseCoeff=" + lambertShader.diffuseCoeff, 2);
                RaiseVerbose("translucenceCoeff=" + lambertShader.translucenceCoeff, 2);

                var babylonMaterial = new BabylonStandardMaterial
                {
                    name = name,
                    id = id,
                    diffuse = lambertShader.color.toArrayRGB(),
                    alpha = 1.0f - lambertShader.transparency[0]
                };

                // Maya ambient <=> babylon emissive
                babylonMaterial.emissive = lambertShader.ambientColor.toArrayRGB();
                babylonMaterial.linkEmissiveWithDiffuse = true; // Incandescence (or Illumination) is not exported

                // If transparency is not a shade of grey (shade of grey <=> R=G=B)
                if (lambertShader.transparency[0] != lambertShader.transparency[1] ||
                    lambertShader.transparency[0] != lambertShader.transparency[2])
                {
                    RaiseWarning("Transparency color is not a shade of grey. Only it's R channel is used.", 2);
                }
                // Convert transparency to opacity
                babylonMaterial.alpha = 1.0f - lambertShader.transparency[0];

                // Specular power
                if (materialObject.hasFn(MFn.Type.kReflect))
                {
                    var reflectShader = new MFnReflectShader(materialObject);

                    RaiseVerbose("specularColor=" + reflectShader.specularColor.toString(), 2);
                    RaiseVerbose("reflectivity=" + reflectShader.reflectivity, 2);
                    RaiseVerbose("reflectedColor=" + reflectShader.reflectedColor.toString(), 2);

                    babylonMaterial.specular = reflectShader.specularColor.toArrayRGB();

                    if (materialObject.hasFn(MFn.Type.kBlinn))
                    {
                        MFnBlinnShader blinnShader = new MFnBlinnShader(materialObject);
                        babylonMaterial.specularPower = (1.0f - blinnShader.eccentricity) * 256;
                    }
                    else if (materialObject.hasFn(MFn.Type.kPhong))
                    {
                        MFnPhongShader phongShader = new MFnPhongShader(materialObject);

                        float glossiness = (float)Math.Log(phongShader.cosPower, 2) * 10;
                        babylonMaterial.specularPower = glossiness / 100 * 256;
                    }
                    else if (materialObject.hasFn(MFn.Type.kPhongExplorer))
                    {
                        MFnPhongEShader phongEShader = new MFnPhongEShader(materialObject);
                        // No use of phongE.whiteness and phongE.highlightSize
                        babylonMaterial.specularPower = (1.0f - phongEShader.roughness) * 256;
                    }
                    else
                    {
                        RaiseWarning("Unknown reflect shader type: " + reflectShader.typeName + ". Specular power is default 64. Consider using a Blinn or Phong shader instead.", 2);
                    }
                }

                // TODO
                //babylonMaterial.backFaceCulling = !stdMat.TwoSided;
                //babylonMaterial.wireframe = stdMat.Wire;

                // --- Textures ---

                babylonMaterial.diffuseTexture = ExportTexture(materialDependencyNode, "color", babylonScene);
                babylonMaterial.emissiveTexture = ExportTexture(materialDependencyNode, "ambientColor", babylonScene); // Maya ambient <=> babylon emissive
                babylonMaterial.bumpTexture = ExportTexture(materialDependencyNode, "normalCamera", babylonScene);
                babylonMaterial.opacityTexture = ExportTexture(materialDependencyNode, "transparency", babylonScene, false, true);
                if (materialObject.hasFn(MFn.Type.kReflect))
                {
                    babylonMaterial.specularTexture = ExportTexture(materialDependencyNode, "specularColor", babylonScene);
                    babylonMaterial.reflectionTexture = ExportTexture(materialDependencyNode, "reflectedColor", babylonScene, true, false, true);
                }

                // Constraints
                if (babylonMaterial.diffuseTexture != null)
                {
                    babylonMaterial.diffuse = new[] { 1.0f, 1.0f, 1.0f };
                }

                if (babylonMaterial.emissiveTexture != null)
                {
                    babylonMaterial.emissive = new float[] { 0, 0, 0 };
                }

                babylonScene.MaterialsList.Add(babylonMaterial);
            }
            // Stingray PBS material
            else if (isStingrayPBSMaterial(materialDependencyNode))
            {
                RaiseMessage("Stingray shader", 2);

                var babylonMaterial = new BabylonPBRMetallicRoughnessMaterial
                {
                    name = name,
                    id = id
                };

                // --- Global ---

                // Color3
                babylonMaterial.baseColor = materialDependencyNode.findPlug("base_color").asFloatArray();

                // Alpha
                string opacityAttributeName = "opacity";
                if (materialDependencyNode.hasAttribute(opacityAttributeName))
                {
                    float opacityAttributeValue = materialDependencyNode.findPlug(opacityAttributeName).asFloatProperty;
                    babylonMaterial.alpha = 1.0f - opacityAttributeValue;
                }

                // Metallic & roughness
                babylonMaterial.metallic = materialDependencyNode.findPlug("metallic").asFloatProperty;
                babylonMaterial.roughness = materialDependencyNode.findPlug("roughness").asFloatProperty;

                // Emissive
                float emissiveIntensity = materialDependencyNode.findPlug("emissive_intensity").asFloatProperty;
                // Factor emissive color with emissive intensity
                emissiveIntensity = Tools.Clamp(emissiveIntensity, 0f, 1f);
                babylonMaterial.emissive = materialDependencyNode.findPlug("emissive").asFloatArray().Multiply(emissiveIntensity);

                // --- Textures ---

                // Base color & alpha
                bool useColorMap = materialDependencyNode.findPlug("use_color_map").asBoolProperty;
                bool useOpacityMap = false;
                string useOpacityMapAttributeName = "use_opacity_map";
                if (materialDependencyNode.hasAttribute(useOpacityMapAttributeName))
                {
                    useOpacityMap = materialDependencyNode.findPlug(useOpacityMapAttributeName).asBoolProperty;
                }
                if (useColorMap || useOpacityMap)
                {
                    // TODO - Force non use map to default value ?
                    // Ex: if useOpacityMap == false, force alpha = 255 for all pixels.
                    //babylonMaterial.baseTexture = ExportBaseColorAlphaTexture(materialDependencyNode, useColorMap, useOpacityMap, babylonMaterial.baseColor, babylonMaterial.alpha, babylonScene);
                    babylonMaterial.baseTexture = ExportTexture(materialDependencyNode, "TEX_color_map", babylonScene, false, useOpacityMap);
                }

                // Metallic & roughness
                bool useMetallicMap = materialDependencyNode.findPlug("use_metallic_map").asBoolProperty;
                bool useRoughnessMap = materialDependencyNode.findPlug("use_roughness_map").asBoolProperty;
                babylonMaterial.metallicRoughnessTexture = ExportMetallicRoughnessTexture(materialDependencyNode, useMetallicMap, useRoughnessMap, babylonScene, name);

                if (materialDependencyNode.findPlug("use_normal_map").asBoolProperty)
                {
                    babylonMaterial.normalTexture = ExportTexture(materialDependencyNode, "TEX_normal_map", babylonScene);
                }

                // Emissive
                bool useEmissiveMap = materialDependencyNode.findPlug("use_emissive_map").asBoolProperty;
                if (useEmissiveMap)
                {
                    babylonMaterial.emissiveTexture = ExportTexture(materialDependencyNode, "TEX_emissive_map", babylonScene, false, false, false, emissiveIntensity);
                }

                // Ambient occlusion
                string useAOMapAttributeName = "use_ao_map";
                if (materialDependencyNode.hasAttribute(useAOMapAttributeName) && materialDependencyNode.findPlug(useAOMapAttributeName).asBoolProperty)
                {
                    babylonMaterial.occlusionTexture = ExportTexture(materialDependencyNode, "TEX_ao_map", babylonScene);
                }

                // Constraints
                if (useColorMap)
                {
                    babylonMaterial.baseColor = new[] { 1.0f, 1.0f, 1.0f };
                }
                if (useOpacityMap)
                {
                    babylonMaterial.alpha = 1.0f;
                }
                if (babylonMaterial.alpha != 1.0f || (babylonMaterial.baseTexture != null && babylonMaterial.baseTexture.hasAlpha))
                {
                    babylonMaterial.transparencyMode = (int)BabylonPBRMetallicRoughnessMaterial.TransparencyMode.ALPHABLEND;
                }
                if (useMetallicMap)
                {
                    babylonMaterial.metallic = 1.0f;
                }
                if (useRoughnessMap)
                {
                    babylonMaterial.roughness = 1.0f;
                }
                if (useEmissiveMap)
                {
                    babylonMaterial.emissive = new[] { 1.0f, 1.0f, 1.0f };
                }

                babylonScene.MaterialsList.Add(babylonMaterial);
            }
            // Arnold Ai Standard Surface
            else if (isAiStandardSurface(materialDependencyNode))
            {
                RaiseMessage("Ai Standard Surface shader", 2);

                var babylonMaterial = new BabylonPBRMetallicRoughnessMaterial
                {
                    name = name,
                    id = id
                };

                // --- Global ---

                // Color3
                float baseWeight = materialDependencyNode.findPlug("base").asFloatProperty;
                float[] baseColor = materialDependencyNode.findPlug("baseColor").asFloatArray();
                babylonMaterial.baseColor = baseColor.Multiply(baseWeight);

                // Alpha
                // Retreive meshes using this material directly
                // Meshes are split into 2 categories: Opaque or Transparent
                List<BabylonMesh> meshesOpaque = new List<BabylonMesh>();
                List<BabylonMesh> meshesTransparent = new List<BabylonMesh>();
                getMeshesOpacity(babylonScene, babylonMaterial.id, meshesOpaque, meshesTransparent);
                // meshHasAlpha is true when at least one mesh is set as Transparent (non Opaque)
                bool meshHasAlpha = meshesTransparent.Count > 0;
                // Material has to be duplicated when assigned to both Opaque and Transparent meshes
                bool duplicationRequired = meshesOpaque.Count > 0 && meshesTransparent.Count > 0;

                // Retreive meshes using this material as a sub material of a multi material
                // Meshes are split into 2 categories: Opaque or Transparent
                List<BabylonMesh> meshesOpaqueMulti = new List<BabylonMesh>();
                List<BabylonMesh> meshesTransparentMulti = new List<BabylonMesh>();
                foreach (KeyValuePair<string, List<MFnDependencyNode>> multiMaterial in multiMaterials)
                {
                    // If the material is part of the sub materials list
                    if (multiMaterial.Value.Find(node => node.uuid().asString() == babylonMaterial.id) != null)
                    {
                        // Then add meshes using this multi material
                        getMeshesOpacity(babylonScene, multiMaterial.Key, meshesOpaqueMulti, meshesTransparentMulti);
                    }
                }
                bool meshHasAlphaMulti = meshesTransparentMulti.Count > 0;
                bool duplicationRequiredMulti = meshesOpaqueMulti.Count > 0 && meshesTransparentMulti.Count > 0;

                // If at least one mesh is Transparent and is using this material either directly or as a sub material
                if (meshHasAlpha || meshHasAlphaMulti)
                {
                    float[] opacityAttributeValue = materialDependencyNode.findPlug("opacity").asFloatArray();
                    babylonMaterial.alpha = opacityAttributeValue[0];
                }
                else
                {
                    // Do not bother about alpha
                    babylonMaterial.alpha = 1.0f;
                }

                // Metallic & roughness
                babylonMaterial.metallic = materialDependencyNode.findPlug("metalness").asFloatProperty;
                babylonMaterial.roughness = materialDependencyNode.findPlug("specularRoughness").asFloatProperty;

                // Emissive
                float emissionWeight = materialDependencyNode.findPlug("emission").asFloatProperty;
                babylonMaterial.emissive = materialDependencyNode.findPlug("emissionColor").asFloatArray().Multiply(emissionWeight);

                // --- Textures ---

                // Base color & alpha
                if (meshHasAlpha || meshHasAlphaMulti)
                {
                    MFnDependencyNode baseColorTextureDependencyNode = getTextureDependencyNode(materialDependencyNode, "baseColor");
                    MFnDependencyNode opacityTextureDependencyNode = getTextureDependencyNode(materialDependencyNode, "opacity");
                    if (baseColorTextureDependencyNode != null && opacityTextureDependencyNode != null &&
                        getSourcePathFromFileTexture(baseColorTextureDependencyNode) == getSourcePathFromFileTexture(opacityTextureDependencyNode))
                    {
                        // If the same file is used for base color and opacity
                        // Base color and alpha are already merged into a single file
                        babylonMaterial.baseTexture = ExportTexture(baseColorTextureDependencyNode, babylonScene, false, true);
                    }
                    else
                    {
                        // Base color and alpha files need to be merged into a single file
                        Color _baseColor = Color.FromArgb((int)baseColor[0] * 255, (int)baseColor[1] * 255, (int)baseColor[2] * 255);
                        babylonMaterial.baseTexture = ExportBaseColorAlphaTexture(baseColorTextureDependencyNode, opacityTextureDependencyNode, babylonScene, name, _baseColor, babylonMaterial.alpha);
                    }
                }
                else
                {
                    // Base color only
                    // Do not bother about alpha
                    babylonMaterial.baseTexture = ExportTexture(materialDependencyNode, "baseColor", babylonScene);
                }

                // Metallic & roughness
                MFnDependencyNode metallicTextureDependencyNode = getTextureDependencyNode(materialDependencyNode, "metalness");
                MFnDependencyNode roughnessTextureDependencyNode = getTextureDependencyNode(materialDependencyNode, "specularRoughness");
                if (metallicTextureDependencyNode != null && roughnessTextureDependencyNode != null &&
                    getSourcePathFromFileTexture(metallicTextureDependencyNode) == getSourcePathFromFileTexture(roughnessTextureDependencyNode))
                {
                    // If the same file is used for metallic and roughness
                    // Then we assume it's an ORM file (Red=Occlusion, Green=Roughness, Blue=Metallic)

                    // Metallic and roughness are already merged into a single file
                    babylonMaterial.metallicRoughnessTexture = ExportTexture(metallicTextureDependencyNode, babylonScene);

                    // Use same file for Ambient occlusion
                    babylonMaterial.occlusionTexture = babylonMaterial.metallicRoughnessTexture;
                }
                else
                {
                    // Metallic and roughness files need to be merged into a single file
                    // Occlusion texture is not exported since aiStandardSurface material doesn't provide input for it
                    babylonMaterial.metallicRoughnessTexture = ExportMetallicRoughnessTexture(metallicTextureDependencyNode, roughnessTextureDependencyNode, babylonScene, name, babylonMaterial.metallic, babylonMaterial.roughness);
                }

                // Normal
                babylonMaterial.normalTexture = ExportTexture(materialDependencyNode, "normalCamera", babylonScene);

                // Emissive
                babylonMaterial.emissiveTexture = ExportTexture(materialDependencyNode, "emissionColor", babylonScene);

                // Constraints
                if (babylonMaterial.baseTexture != null)
                {
                    babylonMaterial.baseColor = new[] { baseWeight, baseWeight, baseWeight };
                    babylonMaterial.alpha = 1.0f;
                }
                if (babylonMaterial.metallicRoughnessTexture != null)
                {
                    babylonMaterial.metallic = 1.0f;
                    babylonMaterial.roughness = 1.0f;
                }
                if (babylonMaterial.emissiveTexture != null)
                {
                    babylonMaterial.emissive = new[] { emissionWeight, emissionWeight, emissionWeight };
                }

                // If at least one mesh is Transparent and is using this material (either directly or as a sub material)
                if ((meshHasAlpha || meshHasAlphaMulti) &&
                    // And material has alpha (either as value or in its texture)
                    (babylonMaterial.alpha != 1.0f || (babylonMaterial.baseTexture != null && babylonMaterial.baseTexture.hasAlpha)))
                {
                    // Since Babylon.Mesh doesn't have an equivalent to Maya.Mesh.aiOpaque
                    // Material has to be duplicated when assigned to both Opaque and Transparent meshes
                    if (duplicationRequired || meshHasAlpha != meshHasAlphaMulti)
                    {
                        // Duplicate material
                        BabylonPBRMetallicRoughnessMaterial babylonMaterialCloned = new BabylonPBRMetallicRoughnessMaterial(babylonMaterial);

                        // Give it a new UUID
                        MUuid mUuid = new MUuid();
                        mUuid.generate();
                        babylonMaterialCloned.id = mUuid.asString();

                        // The duplicated material is used either as Transparent or Opaque
                        // The choice is made so that no modification are made on multimaterials
                        BabylonPBRMetallicRoughnessMaterial materialOpaque = babylonMaterial;
                        BabylonPBRMetallicRoughnessMaterial materialTransparent = babylonMaterialCloned;
                        List<BabylonMesh> meshesToUpdate = meshesTransparent;
                        if (meshHasAlphaMulti)
                        {
                            materialOpaque = babylonMaterialCloned;
                            materialTransparent = babylonMaterial;
                            meshesToUpdate = meshesOpaque;
                        }

                        // Reset alpha for opaque material
                        materialOpaque.alpha = 1;
                        if (materialOpaque.baseTexture != null)
                        {
                            materialOpaque.baseTexture.hasAlpha = false;
                        }

                        // Set transparency for transparent material
                        materialTransparent.transparencyMode = (int)BabylonPBRMetallicRoughnessMaterial.TransparencyMode.ALPHABLEND;

                        // Update meshes material id
                        meshesToUpdate.ForEach(mesh =>
                        {
                            mesh.materialId = babylonMaterialCloned.id;
                        });

                        // Store duplicated material too
                        babylonScene.MaterialsList.Add(babylonMaterialCloned);
                        
                        if (duplicationRequiredMulti)
                        {
                            RaiseError("Material duplication failed: material is part of a multi material assigned to both opaque and non opaque meshes (Arnold attribute). See documentation for more information.", 2);
                        }
                    }
                    else
                    {
                        babylonMaterial.transparencyMode = (int)BabylonPBRMetallicRoughnessMaterial.TransparencyMode.ALPHABLEND;
                    }
                }

                babylonScene.MaterialsList.Add(babylonMaterial);
            }
            else
            {
                RaiseWarning("Unsupported material type '" + materialObject.apiType + "' for material named '" + materialDependencyNode.name + "'", 2);
            }
        }

        private bool isStingrayPBSMaterial(MFnDependencyNode materialDependencyNode)
        {
            // TODO - Find a better way to identify material type
            string graphAttribute = "graph";
            if (materialDependencyNode.hasAttribute(graphAttribute))
            {
                string graphValue = materialDependencyNode.findPlug(graphAttribute).asStringProperty;
                return graphValue.Contains("stingray");
            }
            else
            {
                return false;
            }
        }

        private bool isAiStandardSurface(MFnDependencyNode materialDependencyNode)
        {
            // TODO - Find a better way to identify material type
            return materialDependencyNode.hasAttribute("baseColor") &&
                materialDependencyNode.hasAttribute("metalness") &&
                materialDependencyNode.hasAttribute("normalCamera") &&
                materialDependencyNode.hasAttribute("specularRoughness") &&
                materialDependencyNode.hasAttribute("emissionColor");
        }

        private void getMeshesOpacity(BabylonScene babylonScene, string materialId, List<BabylonMesh> meshesOpaque, List<BabylonMesh> meshesTransparent)
        {
            babylonScene.MeshesList.ForEach(mesh =>
            {
                if (mesh.materialId == materialId)
                {
                    int meshOpaqueInt;
                    MGlobal.executeCommand($@"getAttr {mesh.name}.aiOpaque;", out meshOpaqueInt);
                    if (meshOpaqueInt == 1)
                    {
                        meshesOpaque.Add(mesh);
                    }
                    else
                    {
                        meshesTransparent.Add(mesh);
                    }
                }
            });
        }

        public string GetMultimaterialUUID(List<MFnDependencyNode> materials)
        {
            List<MFnDependencyNode> materialsSorted = new List<MFnDependencyNode>(materials);
            materialsSorted.Sort(new MFnDependencyNodeComparer());

            string uuidConcatenated = "";
            bool isFirstTime = true;
            foreach (MFnDependencyNode material in materialsSorted)
            {
                if (!isFirstTime)
                {
                    uuidConcatenated += "_";
                }
                isFirstTime = false;

                uuidConcatenated += material.uuid().asString();
            }

            return uuidConcatenated;
        }

        public class MFnDependencyNodeComparer : IComparer<MFnDependencyNode>
        {
            public int Compare(MFnDependencyNode x, MFnDependencyNode y)
            {
                return x.uuid().asString().CompareTo(y.uuid().asString());
            }
        }

        public class MFnDependencyNodeEqualityComparer : IEqualityComparer<MFnDependencyNode>
        {
            public bool Equals(MFnDependencyNode x, MFnDependencyNode y)
            {
                return x.uuid().asString() == y.uuid().asString();
            }

            public int GetHashCode(MFnDependencyNode obj)
            {
                return obj.GetHashCode();
            }
        }
    }
}
