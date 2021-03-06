using ICities;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Reflection;
using System.Linq;

using ColossalFramework;
/* Notes

 */
 
namespace HideCrosswalks.Patches {
    using System;
    using UnityEngine;
    using Utils; using KianCommons;
    using static TranspilerUtils;
    public static class CalculateMaterialCommons {
        public static bool ShouldHideCrossing(ushort nodeID, ushort segmentID) {
            // TODO move to netnode.updateflags or something
            NetInfo info = segmentID.ToSegment().Info;

            // this assertion can fail due to race condition (when node controller post chanes node).
            // therefore i am commenting this out.
            // it would be nice to move as much of this code in simulation thread (eg netnode.updateflags)
            //bool isJunction = nodeID.ToNode().m_flags.IsFlagSet(NetNode.Flags.Junction);
            //Assertion.Assert(isJunction, $"isJunction | segmentID:{segmentID} nodeID:{nodeID}");

            bool ret0 = NetInfoExt.GetCanHideMarkings(info);
            bool ret1 = TMPEUTILS.HasCrossingBan(segmentID, nodeID) & NetInfoExt.GetCanHideCrossings(info);
            bool ret2 = ret0 & NS2Utils.HideJunctionMarkings(segmentID);
            bool ret =  ret1 | ret2;
            // Log.Debug($"ShouldHideCrossing segmentID={segmentID} nodeID={nodeID} ret0:{ret0} ret1:{ret1} ret2:{ret2} ret:{ret}");
            return ret;
        }

        public static Material CalculateMaterial(Material material, ushort nodeID, ushort segmentID) {
            if (ShouldHideCrossing(nodeID, segmentID)) {
                NetInfo netInfo = segmentID.ToSegment().Info;
                material = MaterialUtils.HideCrossings(material, null, netInfo, lod:false);
            }
            return material;
        }

        static Type[] args = new [] {
                typeof(Mesh),
                typeof(Vector3),
                typeof(Quaternion),
                typeof(Material),
                typeof(int),
                typeof(Camera),
                typeof(int),
                typeof(MaterialPropertyBlock) };
        static MethodInfo mDrawMesh =
            typeof(Graphics).GetMethod("DrawMesh", args) ?? throw new Exception("mDrawMesh is null");
        static FieldInfo fNodeMaterial =
            typeof(NetInfo.Node).GetField("m_nodeMaterial") ?? throw new Exception("fNodeMaterial is null");
        static MethodInfo mCalculateMaterial =
            typeof(CalculateMaterialCommons).GetMethod("CalculateMaterial") ?? throw new Exception("mCalculateMaterial is null");
        static MethodInfo mCheckRenderDistance =
            typeof(RenderManager.CameraInfo).GetMethod("CheckRenderDistance") ?? throw new Exception("mCheckRenderDistance is null");
        static MethodInfo mShouldHideCrossing =
            typeof(CalculateMaterialCommons).GetMethod("ShouldHideCrossing") ?? throw new Exception("mShouldHideCrossing is null");
        static MethodInfo mGetSegment =
            typeof(NetNode).GetMethod("GetSegment") ?? throw new Exception("mGetSegment is null");

        // returns the position of First DrawMesh after index.
        public static void PatchCheckFlags(List<CodeInstruction> codes, int occurance, MethodInfo method) {
            //Assertion.Assert(mDrawMesh != null, "mDrawMesh!=null failed");
            //Assertion.Assert(fNodeMaterial != null, "fNodeMaterial!=null failed"); 
            //Assertion.Assert(mCalculateMaterial != null, "mCalculateMaterial!=null failed"); 
            //Assertion.Assert(mCheckRenderDistance != null, "mCheckRenderDistance!=null failed"); 
            //Assertion.Assert(mShouldHideCrossing != null, "mShouldHideCrossing!=null failed");

            int index = 0;
            index = SearchInstruction(codes, new CodeInstruction(OpCodes.Call, mDrawMesh), index, counter: occurance);
            Assertion.Assert(index != 0, "index!=0");


            // find ldfld node.m_material
            index = SearchInstruction(codes, new CodeInstruction(OpCodes.Ldfld, fNodeMaterial), index, dir: -1);
            int insertIndex2 = index + 1;

            // find: if (cameraInfo.CheckRenderDistance(data.m_position, node.m_lodRenderDistance))
            /* IL_0627: callvirt instance bool RenderManager CameraInfo::CheckRenderDistance(Vector3, float32)
             * IL_062c brfalse      IL_07e2 */
            index = SearchInstruction(codes, new CodeInstruction(OpCodes.Callvirt, mCheckRenderDistance), index, dir: -1);
            int insertIndex1 = index + 1; // at this point boloean is in stack


            CodeInstruction LDArg_NodeID = GetLDArg(method, "nodeID"); // push nodeID into stack
            CodeInstruction LDLoc_segmentID = BuildSegnentLDLocFromPrevSTLoc(codes, index, counter: 1); // push segmentID into stack

            { // Insert material = CalculateMaterial(material, nodeID, segmentID)
                var newInstructions = new[] {
                    LDArg_NodeID,
                    LDLoc_segmentID,
                    new CodeInstruction(OpCodes.Call, mCalculateMaterial), // call Material CalculateMaterial(material, nodeID, segmentID).
                };
                InsertInstructions(codes, newInstructions, insertIndex2);
            }

            { // Insert ShouldHideCrossing(nodeID, segmentID)
                var newInstructions = new[]{
                    LDArg_NodeID, 
                    LDLoc_segmentID, 
                    new CodeInstruction(OpCodes.Call, mShouldHideCrossing), // call Material mShouldHideCrossing(nodeID, segmentID).
                    new CodeInstruction(OpCodes.Or) };

                InsertInstructions(codes, newInstructions, insertIndex1);
            } // end block
        } // end method

        public static CodeInstruction BuildSegnentLDLocFromPrevSTLoc(List<CodeInstruction> codes, int index, int counter=1) {
            Assertion.Assert(mGetSegment != null, "mGetSegment!=null");
            index = SearchInstruction(codes, new CodeInstruction(OpCodes.Call, mGetSegment), index, counter: counter, dir: -1);

            var code = codes[index + 1];
            Assertion.Assert(IsStLoc(code), $"IsStLoc(code) | code={code}");

            return BuildLdLocFromStLoc(code);
        }



    }
}
