﻿using LegendaryExplorerCore.Kismet;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Packages.CloningImportingAndRelinking;
using LegendaryExplorerCore.Unreal;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CrossGenV.Classes
{
    /// <summary>
    /// Handles Kismet-specific things
    /// </summary>
    public class VTestKismet
    {
        /// <summary>
        /// Checks if the specified sequence object is contained within a named sequence. Can be used to find sequences that are templated embedded within other different sequences.
        /// </summary>
        /// <param name="sequenceObject"></param>
        /// <param name="seqName"></param>
        /// <param name="fullParentChain"></param>
        /// <returns></returns>
        public static bool IsContainedWithinSequenceNamed(ExportEntry sequenceObject, string seqName, bool fullParentChain = true)
        {
            var parent = KismetHelper.GetParentSequence(sequenceObject);
            while (parent != null)
            {
                var parentName = parent.GetProperty<StrProperty>("ObjName");
                if (parentName?.Value == seqName)
                    return true;
                if (!fullParentChain)
                    break;
                parent = KismetHelper.GetParentSequence(parent);
            }

            return false;
        }

        /// <summary>
        /// Gets the sequence name from ObjName
        /// </summary>
        /// <param name="sequence">Export for the sequence</param>
        /// <returns></returns>
        public static string GetSequenceName(ExportEntry sequence)
        {
            return sequence.GetProperty<StrProperty>("ObjName")?.Value;
        }

        public static ExportEntry FindSequenceObjectByClassAndPosition(ExportEntry sequence, string className, int posX = int.MinValue, int posY = int.MinValue)
        {
            var seqObjs = sequence.GetProperty<ArrayProperty<ObjectProperty>>("SequenceObjects")
                .Select(x => x.ResolveToEntry(sequence.FileRef)).OfType<ExportEntry>().Where(x => x.ClassName == className).ToList();

            foreach (var obj in seqObjs)
            {
                if (posX != int.MinValue && posY != int.MinValue)
                {
                    var props = obj.GetProperties();
                    var foundPosX = props.GetProp<IntProperty>("ObjPosX")?.Value;
                    var foundPosY = props.GetProp<IntProperty>("ObjPosY")?.Value;
                    if (foundPosX != null && foundPosY != null &&
                        foundPosX == posX && foundPosY == posY)
                    {
                        return obj;
                    }
                }
                else if (seqObjs.Count == 1)
                {
                    return obj; // First object
                }
                else
                {
                    throw new Exception($"COULD NOT FIND OBJECT OF TYPE {className} in {sequence.InstancedFullPath}");
                }
            }

            return null;
        }

        /// <summary>
        /// Adds a ActivateRemoteEvent kismet object as an output of the specified IFP.
        /// </summary>
        /// <param name="le1File"></param>
        /// <param name="vTestOptions"></param>
        public static void InstallRemoteEventSignal(IMEPackage le1File, string sourceIFP, string remoteEventName, VTestOptions vTestOptions, string outlinkName = "Out")
        {
            var source = le1File.FindExport(sourceIFP);
            var sequence = KismetHelper.GetParentSequence(source);
            var remoteEvent = SequenceObjectCreator.CreateSequenceObject(le1File, "SeqAct_ActivateRemoteEvent", vTestOptions.cache);
            KismetHelper.AddObjectToSequence(remoteEvent, sequence);
            remoteEvent.WriteProperty(new NameProperty(remoteEventName, "EventName"));
            KismetHelper.CreateOutputLink(source, outlinkName, remoteEvent);
        }

        /// <summary>
        /// Sets up sequencing to stream in the listed materials for 5 seconds in the specified stream
        /// </summary>
        /// <param name="sequence"></param>
        /// <param name="materialsToStreamIn"></param>
        public static void CreateSignaledTextureStreaming(ExportEntry sequence, string[] materialsToStreamIn, VTestOptions vTestOptions)
        {

            var remoteEvent = SequenceObjectCreator.CreateSequenceObject(sequence.FileRef, "SeqEvent_RemoteEvent", vTestOptions.cache);
            var streamInTextures = SequenceObjectCreator.CreateSequenceObject(sequence.FileRef, "SeqAct_StreamInTextures", vTestOptions.cache);

            KismetHelper.AddObjectToSequence(remoteEvent, sequence);
            KismetHelper.AddObjectToSequence(streamInTextures, sequence);

            streamInTextures.WriteProperty(new FloatProperty(5f, "Seconds")); // Force textures to stream in at full res for a bit over the load screen time
            var materials = new ArrayProperty<ObjectProperty>("ForceMaterials");
            foreach (var matIFP in materialsToStreamIn)
            {
                var entry = sequence.FileRef.FindEntry(matIFP);
                if (entry == null) Debugger.Break(); // THIS SHOULDN'T HAPPEN
                materials.Add(new ObjectProperty(entry));
            }
            streamInTextures.WriteProperty(materials);

            remoteEvent.WriteProperty(new NameProperty("CROSSGEN_PrepTextures", "EventName"));

            KismetHelper.CreateOutputLink(remoteEvent, "Out", streamInTextures);

        }

        /// <summary>
        /// Installs a sequence from VTestHelper. The sequence will be connected via the In pin.
        /// </summary>
        /// <param name="le1File"></param>
        /// <param name="sourceSequenceOpIFP"></param>
        /// <param name="vTestSequenceIFP"></param>
        /// <param name="vTestOptions"></param>
        public static void InstallVTestHelperSequenceViaOut(IMEPackage le1File, string sourceSequenceOpIFP, string vTestSequenceIFP, bool runOnceOnly, VTestOptions vTestOptions, out ExportEntry gate, bool addInline = false)
        {
            gate = null;
            var sourceItemToOutFrom = le1File.FindExport(sourceSequenceOpIFP);
            var parentSequence = KismetHelper.GetParentSequence(sourceItemToOutFrom, true);
            var donorSequence = vTestOptions.vTestHelperPackage.FindExport(vTestSequenceIFP);
            EntryImporter.ImportAndRelinkEntries(EntryImporter.PortingOption.CloneAllDependencies, donorSequence, le1File, parentSequence, true, new RelinkerOptionsPackage() { Cache = vTestOptions.cache }, out var newUiSeq);
            KismetHelper.AddObjectToSequence(newUiSeq as ExportEntry, parentSequence);

            if (addInline)
            {
                KismetHelper.CreateOutputLink(newUiSeq as ExportEntry, "Out", KismetHelper.GetOutputLinksOfNode(sourceItemToOutFrom)[0][0].LinkedOp as ExportEntry);
                KismetHelper.RemoveOutputLinks(sourceItemToOutFrom);
            }

            if (runOnceOnly)
            {
                gate = SequenceObjectCreator.CreateSequenceObject(le1File, "SeqAct_Gate", vTestOptions.cache);
                KismetHelper.AddObjectToSequence(gate, parentSequence);
                // link it up
                KismetHelper.CreateOutputLink(sourceItemToOutFrom, "Out", gate);
                KismetHelper.CreateOutputLink(gate, "Out", gate, 2); // close self
                KismetHelper.CreateOutputLink(gate, "Out", newUiSeq as ExportEntry);
            }
            else
            {
                // link it up
                KismetHelper.CreateOutputLink(sourceItemToOutFrom, "Out", newUiSeq as ExportEntry);
            }
        }

        /// <summary>
        /// Installs a sequence from VTestHelper. The sequence should already contain it's own triggers like LevelLoaded.
        /// </summary>
        /// <param name="le1File"></param>
        /// <param name="eventIFP"></param>
        /// <param name="vTestSequenceIFP"></param>
        /// <param name="vTestOptions"></param>
        public static ExportEntry InstallVTestHelperSequenceNoInput(IMEPackage le1File, string sequenceIFP, string vTestSequenceIFP, VTestOptions vTestOptions)
        {
            var sequence = le1File.FindExport(sequenceIFP);
            var donorSequence = vTestOptions.vTestHelperPackage.FindExport(vTestSequenceIFP);
            EntryImporter.ImportAndRelinkEntries(EntryImporter.PortingOption.CloneAllDependencies, donorSequence, le1File, sequence, true, new RelinkerOptionsPackage() { Cache = vTestOptions.cache }, out var newUiSeq);
            KismetHelper.AddObjectToSequence(newUiSeq as ExportEntry, sequence);
            return newUiSeq as ExportEntry;
        }

        /// <summary>
        /// Installs a sequence from VTestHelper. The sequence should already contain it's own triggers like LevelLoaded.
        /// </summary>
        /// <param name="le1File"></param>
        /// <param name="eventIFP"></param>
        /// <param name="vTestSequenceIFP"></param>
        /// <param name="vTestOptions"></param>
        public static ExportEntry InstallVTestHelperSequenceViaEvent(IMEPackage le1File, string eventIFP, string vTestSequenceIFP, VTestOptions vTestOptions, string outName = "Out")
        {
            var targetEvent = le1File.FindExport(eventIFP);
            var sequence = KismetHelper.GetParentSequence(targetEvent);
            var donorSequence = vTestOptions.vTestHelperPackage.FindExport(vTestSequenceIFP);
            EntryImporter.ImportAndRelinkEntries(EntryImporter.PortingOption.CloneAllDependencies, donorSequence, le1File, sequence, true, new RelinkerOptionsPackage() { Cache = vTestOptions.cache }, out var newUiSeq);
            KismetHelper.AddObjectToSequence(newUiSeq as ExportEntry, sequence);
            KismetHelper.CreateOutputLink(targetEvent, outName, newUiSeq as ExportEntry);
            return newUiSeq as ExportEntry;
        }

        /// <summary>
        /// Returns list of sequence references that reference a sequence with the given name
        /// </summary>
        /// <param name="seq"></param>
        /// <param name="sequenceName"></param>
        /// <returns></returns>
        public static List<ExportEntry> GetSequenceObjectReferences(ExportEntry seq, string sequenceName)
        {
            var seqObjs = KismetHelper.GetSequenceObjects(seq).OfType<ExportEntry>().ToList();
            var seqRefs = seqObjs.Where(x => x.ClassName == "SequenceReference");
            var references = seqRefs.Where(x =>
                x.GetProperty<ObjectProperty>("oSequenceReference") is ObjectProperty op && op?.Value != 0 &&
                seq.FileRef.GetUExport(op.Value) is ExportEntry sequence &&
                sequence.GetProperty<StrProperty>("ObjName")?.Value == sequenceName).ToList();
            return references;
        }


        public static void HookupLog(ExportEntry logObj, string message, ExportEntry floatVal = null, ExportEntry intVal = null, PackageCache cache = null)
        {
            var seq = KismetHelper.GetParentSequence(logObj);
            var str = SequenceObjectCreator.CreateString(seq, message, cache);
            KismetHelper.CreateVariableLink(logObj, "String", str);
            if (floatVal != null)
            {
                KismetHelper.CreateVariableLink(logObj, "Float", floatVal);
            }
            if (intVal != null)
            {
                KismetHelper.CreateVariableLink(logObj, "Int", intVal);
            }
        }

        /// <summary>
        /// Ports a sequence object from VTestHelper package into the target sequence
        /// </summary>
        /// <param name="seq"></param>
        /// <param name="helperIFP"></param>
        /// <param name="vTestOptions"></param>
        /// <returns></returns>
        public static ExportEntry AddHelperObjectToSequence(ExportEntry seq, string helperIFP, VTestOptions vTestOptions)
        {
            var helperObj = vTestOptions.vTestHelperPackage.FindExport(helperIFP);
            EntryImporter.ImportAndRelinkEntries(EntryImporter.PortingOption.CloneAllDependencies, helperObj, seq.FileRef, seq, true, new RelinkerOptionsPackage() { Cache = vTestOptions.cache }, out var newObj);
            KismetHelper.AddObjectToSequence(newObj as ExportEntry, seq);
            return newObj as ExportEntry;
        }

        public static string GetSequenceFullPath(ExportEntry seq)
        {
            string ret = "";
            IEntry parent = seq;
            while (parent is ExportEntry exp)
            {
                var objName = GetSequenceName(exp);
                if (ret == "")
                {
                    // Leaf
                    ret = objName ?? parent.ObjectName.Instanced;
                }
                else
                {
                    // Not leaf
                    ret = $"{objName ?? parent.ObjectName.Instanced}.{ret}";
                }
                parent = parent.Parent;
            }

            return ret;
        }
    }
}
