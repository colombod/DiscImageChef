﻿// /***************************************************************************
// The Disc Image Chef
// ----------------------------------------------------------------------------
//
// Filename       : CreateSidecar.cs
// Version        : 1.0
// Author(s)      : Natalia Portillo
//
// Component      : Component
//
// Revision       : $Revision$
// Last change by : $Author$
// Date           : $Date$
//
// --[ Description ] ----------------------------------------------------------
//
// Description
//
// --[ License ] --------------------------------------------------------------
//
//     This program is free software: you can redistribute it and/or modify
//     it under the terms of the GNU General Public License as
//     published by the Free Software Foundation, either version 3 of the
//     License, or (at your option) any later version.
//
//     This program is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//
//     You should have received a copy of the GNU General Public License
//     along with this program.  If not, see <http://www.gnu.org/licenses/>.
//
// ----------------------------------------------------------------------------
// Copyright (C) 2011-2015 Claunia.com
// ****************************************************************************/
// //$Id$
using System;
using Schemas;
using System.Collections.Generic;
using DiscImageChef.Plugins;
using DiscImageChef.ImagePlugins;
using DiscImageChef.Console;
using DiscImageChef.Checksums;
using System.IO;
using System.Threading;
using DiscImageChef.CommonTypes;
using DiscImageChef.PartPlugins;

namespace DiscImageChef.Commands
{
    public static class CreateSidecar
    {
        public static void doSidecar(CreateSidecarSubOptions options)
        {
            CICMMetadataType sidecar = new CICMMetadataType();
            PluginBase plugins = new PluginBase();
            plugins.RegisterAllPlugins();
            ImagePlugin _imageFormat;

            if (!System.IO.File.Exists(options.InputFile))
            {
                DicConsole.ErrorWriteLine("Specified file does not exist.");
                return;
            }

            try
            {
                _imageFormat = ImageFormat.Detect(options.InputFile);

                if (_imageFormat == null)
                {
                    DicConsole.WriteLine("Image format not identified, not proceeding with analysis.");
                    return;
                }
                else
                {
                    if (options.Verbose)
                        DicConsole.VerboseWriteLine("Image format identified by {0} ({1}).", _imageFormat.Name, _imageFormat.PluginUUID);
                    else
                        DicConsole.WriteLine("Image format identified by {0}.", _imageFormat.Name);
                }

                try
                {
                    if (!_imageFormat.OpenImage(options.InputFile))
                    {
                        DicConsole.WriteLine("Unable to open image format");
                        DicConsole.WriteLine("No error given");
                        return;
                    }

                    DicConsole.DebugWriteLine("Analyze command", "Correctly opened image file.");
                }
                catch (Exception ex)
                {
                    DicConsole.ErrorWriteLine("Unable to open image format");
                    DicConsole.ErrorWriteLine("Error: {0}", ex.Message);
                    return;
                }

                Core.Statistics.AddMediaFormat(_imageFormat.GetImageFormat());

                FileInfo fi = new FileInfo(options.InputFile);
                FileStream fs = new FileStream(options.InputFile, FileMode.Open, FileAccess.Read);

                Core.Checksum imgChkWorker = new DiscImageChef.Core.Checksum();

                byte[] data;
                long position = 0;
                while (position < (fi.Length - 1048576))
                {
                    data = new byte[1048576];
                    fs.Read(data, 0, 1048576);

                    DicConsole.Write("\rHashing image file byte {0} of {1}", position, fi.Length);

                    imgChkWorker.Update(data);

                    position += 1048576;
                }

                data = new byte[fi.Length - position];
                fs.Read(data, 0, (int)(fi.Length - position));

                DicConsole.Write("\rHashing image file byte {0} of {1}", position, fi.Length);

                imgChkWorker.Update(data);

                DicConsole.WriteLine();
                fs.Close();

                List<ChecksumType> imgChecksums = imgChkWorker.End();

                switch (_imageFormat.ImageInfo.xmlMediaType)
                {
                    case XmlMediaType.OpticalDisc:
                        {
                            sidecar.OpticalDisc = new OpticalDiscType[1];
                            sidecar.OpticalDisc[0] = new OpticalDiscType();
                            sidecar.OpticalDisc[0].Checksums = imgChecksums.ToArray();
                            sidecar.OpticalDisc[0].Image = new ImageType();
                            sidecar.OpticalDisc[0].Image.format = _imageFormat.GetImageFormat();
                            sidecar.OpticalDisc[0].Image.offset = 0;
                            sidecar.OpticalDisc[0].Image.offsetSpecified = true;
                            sidecar.OpticalDisc[0].Image.Value = Path.GetFileName(options.InputFile);
                            sidecar.OpticalDisc[0].Size = fi.Length;
                            sidecar.OpticalDisc[0].Sequence = new SequenceType();
                            if (_imageFormat.GetMediaSequence() != 0 && _imageFormat.GetLastDiskSequence() != 0)
                            {
                                sidecar.OpticalDisc[0].Sequence.MediaSequence = _imageFormat.GetMediaSequence();
                                sidecar.OpticalDisc[0].Sequence.TotalMedia = _imageFormat.GetMediaSequence();
                            }
                            else
                            {
                                sidecar.OpticalDisc[0].Sequence.MediaSequence = 1;
                                sidecar.OpticalDisc[0].Sequence.TotalMedia = 1;
                            }
                            sidecar.OpticalDisc[0].Sequence.MediaTitle = _imageFormat.GetImageName();

                            MediaType dskType = _imageFormat.ImageInfo.mediaType;

                            foreach (MediaTagType tagType in _imageFormat.ImageInfo.readableMediaTags)
                            {
                                switch (tagType)
                                {
                                    case MediaTagType.CD_ATIP:
                                        sidecar.OpticalDisc[0].ATIP = new DumpType();
                                        sidecar.OpticalDisc[0].ATIP.Checksums = Core.Checksum.GetChecksums(_imageFormat.ReadDiskTag(MediaTagType.CD_ATIP)).ToArray();
                                        sidecar.OpticalDisc[0].ATIP.Size = _imageFormat.ReadDiskTag(MediaTagType.CD_ATIP).Length;
                                        Decoders.CD.ATIP.CDATIP? atip = Decoders.CD.ATIP.Decode(_imageFormat.ReadDiskTag(MediaTagType.CD_ATIP));
                                        if (atip.HasValue)
                                        {
                                            if (atip.Value.DDCD)
                                                dskType = atip.Value.DiscType ? MediaType.DDCDRW : MediaType.DDCDR;
                                            else
                                                dskType = atip.Value.DiscType ? MediaType.CDRW : MediaType.CDR;
                                        }
                                        break;
                                    case MediaTagType.DVD_BCA:
                                        sidecar.OpticalDisc[0].BCA = new DumpType();
                                        sidecar.OpticalDisc[0].BCA.Checksums = Core.Checksum.GetChecksums(_imageFormat.ReadDiskTag(MediaTagType.DVD_BCA)).ToArray();
                                        sidecar.OpticalDisc[0].BCA.Size = _imageFormat.ReadDiskTag(MediaTagType.DVD_BCA).Length;
                                        break;
                                    case MediaTagType.BD_BCA:
                                        sidecar.OpticalDisc[0].BCA = new DumpType();
                                        sidecar.OpticalDisc[0].BCA.Checksums = Core.Checksum.GetChecksums(_imageFormat.ReadDiskTag(MediaTagType.BD_BCA)).ToArray();
                                        sidecar.OpticalDisc[0].BCA.Size = _imageFormat.ReadDiskTag(MediaTagType.BD_BCA).Length;
                                        break;
                                    case MediaTagType.DVD_CMI:
                                        sidecar.OpticalDisc[0].CMI = new DumpType();
                                        Decoders.DVD.CSS_CPRM.LeadInCopyright? cmi = Decoders.DVD.CSS_CPRM.DecodeLeadInCopyright(_imageFormat.ReadDiskTag(MediaTagType.DVD_CMI));
                                        if (cmi.HasValue)
                                        {
                                            switch (cmi.Value.CopyrightType)
                                            {
                                                case Decoders.DVD.CopyrightType.AACS:
                                                    sidecar.OpticalDisc[0].CopyProtection = "AACS";
                                                    break;
                                                case Decoders.DVD.CopyrightType.CSS:
                                                    sidecar.OpticalDisc[0].CopyProtection = "CSS";
                                                    break;
                                                case Decoders.DVD.CopyrightType.CPRM:
                                                    sidecar.OpticalDisc[0].CopyProtection = "CPRM";
                                                    break;
                                            }
                                        }
                                        sidecar.OpticalDisc[0].CMI.Checksums = Core.Checksum.GetChecksums(_imageFormat.ReadDiskTag(MediaTagType.DVD_CMI)).ToArray();
                                        sidecar.OpticalDisc[0].CMI.Size = _imageFormat.ReadDiskTag(MediaTagType.DVD_CMI).Length;
                                        break;
                                    case MediaTagType.DVD_DMI:
                                        sidecar.OpticalDisc[0].DMI = new DumpType();
                                        sidecar.OpticalDisc[0].DMI.Checksums = Core.Checksum.GetChecksums(_imageFormat.ReadDiskTag(MediaTagType.DVD_DMI)).ToArray();
                                        sidecar.OpticalDisc[0].DMI.Size = _imageFormat.ReadDiskTag(MediaTagType.DVD_DMI).Length;
                                        if (Decoders.Xbox.DMI.IsXbox(_imageFormat.ReadDiskTag(MediaTagType.DVD_DMI)))
                                        {
                                            dskType = MediaType.XGD;
                                            sidecar.OpticalDisc[0].Dimensions = new DimensionsType();
                                            sidecar.OpticalDisc[0].Dimensions.Diameter = 120;
                                        }
                                        else if (Decoders.Xbox.DMI.IsXbox360(_imageFormat.ReadDiskTag(MediaTagType.DVD_DMI)))
                                        {
                                            dskType = MediaType.XGD2;
                                            sidecar.OpticalDisc[0].Dimensions = new DimensionsType();
                                            sidecar.OpticalDisc[0].Dimensions.Diameter = 120;
                                        }
                                        break;
                                    case MediaTagType.DVD_PFI:
                                        sidecar.OpticalDisc[0].PFI = new DumpType();
                                        sidecar.OpticalDisc[0].PFI.Checksums = Core.Checksum.GetChecksums(_imageFormat.ReadDiskTag(MediaTagType.DVD_PFI)).ToArray();
                                        sidecar.OpticalDisc[0].PFI.Size = _imageFormat.ReadDiskTag(MediaTagType.DVD_PFI).Length;
                                        Decoders.DVD.PFI.PhysicalFormatInformation? pfi = Decoders.DVD.PFI.Decode(_imageFormat.ReadDiskTag(MediaTagType.DVD_PFI));
                                        if (pfi.HasValue)
                                        {
                                            if (dskType != MediaType.XGD &&
                                                dskType != MediaType.XGD2 &&
                                                dskType != MediaType.XGD3)
                                            {
                                                switch (pfi.Value.DiskCategory)
                                                {
                                                    case Decoders.DVD.DiskCategory.DVDPR:
                                                        dskType = MediaType.DVDPR;
                                                        break;
                                                    case Decoders.DVD.DiskCategory.DVDPRDL:
                                                        dskType = MediaType.DVDPRDL;
                                                        break;
                                                    case Decoders.DVD.DiskCategory.DVDPRW:
                                                        dskType = MediaType.DVDPRW;
                                                        break;
                                                    case Decoders.DVD.DiskCategory.DVDPRWDL:
                                                        dskType = MediaType.DVDPRWDL;
                                                        break;
                                                    case Decoders.DVD.DiskCategory.DVDR:
                                                        dskType = MediaType.DVDR;
                                                        break;
                                                    case Decoders.DVD.DiskCategory.DVDRAM:
                                                        dskType = MediaType.DVDRAM;
                                                        break;
                                                    case Decoders.DVD.DiskCategory.DVDROM:
                                                        dskType = MediaType.DVDROM;
                                                        break;
                                                    case Decoders.DVD.DiskCategory.DVDRW:
                                                        dskType = MediaType.DVDRW;
                                                        break;
                                                    case Decoders.DVD.DiskCategory.HDDVDR:
                                                        dskType = MediaType.HDDVDR;
                                                        break;
                                                    case Decoders.DVD.DiskCategory.HDDVDRAM:
                                                        dskType = MediaType.HDDVDRAM;
                                                        break;
                                                    case Decoders.DVD.DiskCategory.HDDVDROM:
                                                        dskType = MediaType.HDDVDROM;
                                                        break;
                                                    case Decoders.DVD.DiskCategory.HDDVDRW:
                                                        dskType = MediaType.HDDVDRW;
                                                        break;
                                                    case Decoders.DVD.DiskCategory.Nintendo:
                                                        dskType = MediaType.GOD;
                                                        break;
                                                    case Decoders.DVD.DiskCategory.UMD:
                                                        dskType = MediaType.UMD;
                                                        break;
                                                }

                                                if (dskType == MediaType.DVDR && pfi.Value.PartVersion == 6)
                                                    dskType = MediaType.DVDRDL;
                                                if (dskType == MediaType.DVDRW && pfi.Value.PartVersion == 3)
                                                    dskType = MediaType.DVDRWDL;
                                                if (dskType == MediaType.GOD && pfi.Value.DiscSize == DiscImageChef.Decoders.DVD.DVDSize.OneTwenty)
                                                    dskType = MediaType.WOD;

                                                sidecar.OpticalDisc[0].Dimensions = new DimensionsType();
                                                if (dskType == MediaType.UMD)
                                                    sidecar.OpticalDisc[0].Dimensions.Diameter = 60;
                                                else if (pfi.Value.DiscSize == DiscImageChef.Decoders.DVD.DVDSize.Eighty)
                                                    sidecar.OpticalDisc[0].Dimensions.Diameter = 80;
                                                else if (pfi.Value.DiscSize == DiscImageChef.Decoders.DVD.DVDSize.OneTwenty)
                                                    sidecar.OpticalDisc[0].Dimensions.Diameter = 120;
                                            }
                                        }
                                        break;
                                    case MediaTagType.CD_PMA:
                                        sidecar.OpticalDisc[0].PMA = new DumpType();
                                        sidecar.OpticalDisc[0].PMA.Checksums = Core.Checksum.GetChecksums(_imageFormat.ReadDiskTag(MediaTagType.CD_PMA)).ToArray();
                                        sidecar.OpticalDisc[0].PMA.Size = _imageFormat.ReadDiskTag(MediaTagType.CD_PMA).Length;
                                        break;
                                }
                            }

                            string dscType, dscSubType;
                            Metadata.MediaType.MediaTypeToString(dskType, out dscType, out dscSubType);
                            sidecar.OpticalDisc[0].DiscType = dscType;
                            sidecar.OpticalDisc[0].DiscSubType = dscSubType;
                            Core.Statistics.AddMedia(dskType, false);

                            try
                            {
                                List<Session> sessions = _imageFormat.GetSessions();
                                sidecar.OpticalDisc[0].Sessions = sessions != null ? sessions.Count : 1;
                            }
                            catch
                            {
                                sidecar.OpticalDisc[0].Sessions = 1;
                            }

                            List<Track> tracks = _imageFormat.GetTracks();
                            List<Schemas.TrackType> trksLst = null;
                            if (tracks != null)
                            {
                                sidecar.OpticalDisc[0].Tracks = new int[1];
                                sidecar.OpticalDisc[0].Tracks[0] = tracks.Count;
                                trksLst = new List<Schemas.TrackType>();
                            }

                            foreach (Track trk in tracks)
                            {
                                Schemas.TrackType xmlTrk = new Schemas.TrackType();
                                switch (trk.TrackType)
                                {
                                    case DiscImageChef.ImagePlugins.TrackType.Audio:
                                        xmlTrk.TrackType1 = TrackTypeTrackType.audio;
                                        break;
                                    case DiscImageChef.ImagePlugins.TrackType.CDMode2Form2:
                                        xmlTrk.TrackType1 = TrackTypeTrackType.m2f2;
                                        break;
                                    case DiscImageChef.ImagePlugins.TrackType.CDMode2Formless:
                                        xmlTrk.TrackType1 = TrackTypeTrackType.mode2;
                                        break;
                                    case DiscImageChef.ImagePlugins.TrackType.CDMode2Form1:
                                        xmlTrk.TrackType1 = TrackTypeTrackType.m2f1;
                                        break;
                                    case DiscImageChef.ImagePlugins.TrackType.CDMode1:
                                        xmlTrk.TrackType1 = TrackTypeTrackType.mode1;
                                        break;
                                    case DiscImageChef.ImagePlugins.TrackType.Data:
                                        switch (sidecar.OpticalDisc[0].DiscType)
                                        {
                                            case "BD":
                                                xmlTrk.TrackType1 = TrackTypeTrackType.bluray;
                                                break;
                                            case "DDCD":
                                                xmlTrk.TrackType1 = TrackTypeTrackType.ddcd;
                                                break;
                                            case "DVD":
                                                xmlTrk.TrackType1 = TrackTypeTrackType.dvd;
                                                break;
                                            case "HD DVD":
                                                xmlTrk.TrackType1 = TrackTypeTrackType.hddvd;
                                                break;
                                            default:
                                                xmlTrk.TrackType1 = TrackTypeTrackType.mode1;
                                                break;
                                        }
                                        break;
                                }
                                xmlTrk.Sequence = new TrackSequenceType();
                                xmlTrk.Sequence.Session = trk.TrackSession;
                                xmlTrk.Sequence.TrackNumber = (int)trk.TrackSequence;
                                xmlTrk.StartSector = (long)trk.TrackStartSector;
                                xmlTrk.EndSector = (long)trk.TrackEndSector;

                                if (sidecar.OpticalDisc[0].DiscType == "CD" ||
                                    sidecar.OpticalDisc[0].DiscType == "GD")
                                {
                                    xmlTrk.StartMSF = LbaToMsf(xmlTrk.StartSector);
                                    xmlTrk.EndMSF = LbaToMsf(xmlTrk.EndSector);
                                }
                                else if (sidecar.OpticalDisc[0].DiscType == "DDCD")
                                {
                                    xmlTrk.StartMSF = DdcdLbaToMsf(xmlTrk.StartSector);
                                    xmlTrk.EndMSF = DdcdLbaToMsf(xmlTrk.EndSector);
                                }

                                xmlTrk.Image = new ImageType();
                                xmlTrk.Image.Value = Path.GetFileName(trk.TrackFile);
                                if (trk.TrackFileOffset > 0)
                                {
                                    xmlTrk.Image.offset = (long)trk.TrackFileOffset;
                                    xmlTrk.Image.offsetSpecified = true;
                                }

                                xmlTrk.Image.format = trk.TrackFileType;
                                xmlTrk.Size = (xmlTrk.EndSector - xmlTrk.StartSector + 1) * trk.TrackRawBytesPerSector;
                                xmlTrk.BytesPerSector = trk.TrackBytesPerSector;

                                // For fast debugging, skip checksum
                                //goto skipChecksum;

                                uint sectorsToRead = 512;

                                Core.Checksum trkChkWorker = new DiscImageChef.Core.Checksum();

                                ulong sectors = (ulong)(xmlTrk.EndSector - xmlTrk.StartSector + 1);
                                ulong doneSectors = 0;

                                while (doneSectors < sectors)
                                {
                                    byte[] sector;

                                    if ((sectors - doneSectors) >= sectorsToRead)
                                    {
                                        sector = _imageFormat.ReadSectorsLong(doneSectors, sectorsToRead, (uint)xmlTrk.Sequence.TrackNumber);
                                        DicConsole.Write("\rHashings sectors {0} to {2} of track {1} ({3} sectors)", doneSectors, xmlTrk.Sequence.TrackNumber, doneSectors + sectorsToRead, sectors);
                                        doneSectors += sectorsToRead;
                                    }
                                    else
                                    {
                                        sector = _imageFormat.ReadSectorsLong(doneSectors, (uint)(sectors - doneSectors), (uint)xmlTrk.Sequence.TrackNumber);
                                        DicConsole.Write("\rHashings sectors {0} to {2} of track {1} ({3} sectors)", doneSectors, xmlTrk.Sequence.TrackNumber, doneSectors + (sectors - doneSectors), sectors);
                                        doneSectors += (sectors - doneSectors);
                                    }

                                    trkChkWorker.Update(sector);
                                }

                                List<ChecksumType> trkChecksums = trkChkWorker.End();

                                xmlTrk.Checksums = trkChecksums.ToArray();

                                DicConsole.WriteLine();

                                if (trk.TrackSubchannelType != TrackSubchannelType.None)
                                {
                                    xmlTrk.SubChannel = new SubChannelType();
                                    xmlTrk.SubChannel.Image = new ImageType();
                                    switch (trk.TrackSubchannelType)
                                    {
                                        case TrackSubchannelType.Packed:
                                        case TrackSubchannelType.PackedInterleaved:
                                            xmlTrk.SubChannel.Image.format = "rw";
                                            break;
                                        case TrackSubchannelType.Raw:
                                        case TrackSubchannelType.RawInterleaved:
                                            xmlTrk.SubChannel.Image.format = "rw_raw";
                                            break;
                                    }

                                    if (trk.TrackFileOffset > 0)
                                    {
                                        xmlTrk.SubChannel.Image.offset = (long)trk.TrackSubchannelOffset;
                                        xmlTrk.SubChannel.Image.offsetSpecified = true;
                                    }
                                    xmlTrk.SubChannel.Image.Value = trk.TrackSubchannelFile;

                                    // TODO: Packed subchannel has different size?
                                    xmlTrk.SubChannel.Size = (xmlTrk.EndSector - xmlTrk.StartSector + 1) * 96;

                                    Core.Checksum subChkWorker = new DiscImageChef.Core.Checksum();

                                    sectors = (ulong)(xmlTrk.EndSector - xmlTrk.StartSector + 1);
                                    doneSectors = 0;

                                    while (doneSectors < sectors)
                                    {
                                        byte[] sector;

                                        if ((sectors - doneSectors) >= sectorsToRead)
                                        {
                                            sector = _imageFormat.ReadSectorsTag(doneSectors, sectorsToRead, (uint)xmlTrk.Sequence.TrackNumber, SectorTagType.CDSectorSubchannel);
                                            DicConsole.Write("\rHashings subchannel sectors {0} to {2} of track {1} ({3} sectors)", doneSectors, xmlTrk.Sequence.TrackNumber, doneSectors + sectorsToRead, sectors);
                                            doneSectors += sectorsToRead;
                                        }
                                        else
                                        {
                                            sector = _imageFormat.ReadSectorsTag(doneSectors, (uint)(sectors - doneSectors), (uint)xmlTrk.Sequence.TrackNumber, SectorTagType.CDSectorSubchannel);
                                            DicConsole.Write("\rHashings subchannel sectors {0} to {2} of track {1} ({3} sectors)", doneSectors, xmlTrk.Sequence.TrackNumber, doneSectors + (sectors - doneSectors), sectors);
                                            doneSectors += (sectors - doneSectors);
                                        }

                                        subChkWorker.Update(sector);
                                    }

                                    List<ChecksumType> subChecksums = subChkWorker.End();

                                    xmlTrk.SubChannel.Checksums = subChecksums.ToArray();

                                    DicConsole.WriteLine();
                                }

                                // For fast debugging, skip checksum
                                //skipChecksum:

                                DicConsole.WriteLine("Checking filesystems on track {0} from sector {1} to {2}", xmlTrk.Sequence.TrackNumber, xmlTrk.StartSector, xmlTrk.EndSector);

                                List<Partition> partitions = new List<Partition>();

                                foreach (PartPlugin _partplugin in plugins.PartPluginsList.Values)
                                {
                                    List<Partition> _partitions;

                                    if (_partplugin.GetInformation(_imageFormat, out _partitions))
                                    {
                                        partitions = _partitions;
                                        Core.Statistics.AddPartition(_partplugin.Name);
                                        break;
                                    }
                                }

                                xmlTrk.FileSystemInformation = new PartitionType[1];
                                if (partitions.Count > 0)
                                {
                                    xmlTrk.FileSystemInformation = new PartitionType[partitions.Count];
                                    for (int i = 0; i < partitions.Count; i++)
                                    {
                                        xmlTrk.FileSystemInformation[i] = new PartitionType();
                                        xmlTrk.FileSystemInformation[i].Description = partitions[i].PartitionDescription;
                                        xmlTrk.FileSystemInformation[i].EndSector = (int)(partitions[i].PartitionStartSector + partitions[i].PartitionSectors - 1);
                                        xmlTrk.FileSystemInformation[i].Name = partitions[i].PartitionName;
                                        xmlTrk.FileSystemInformation[i].Sequence = (int)partitions[i].PartitionSequence;
                                        xmlTrk.FileSystemInformation[i].StartSector = (int)partitions[i].PartitionStartSector;
                                        xmlTrk.FileSystemInformation[i].Type = partitions[i].PartitionType;

                                        List<FileSystemType> lstFs = new List<FileSystemType>();

                                        foreach (Plugin _plugin in plugins.PluginsList.Values)
                                        {
                                            try
                                            {
                                                if (_plugin.Identify(_imageFormat, partitions[i].PartitionStartSector, partitions[i].PartitionStartSector + partitions[i].PartitionSectors - 1))
                                                {
                                                    string foo;
                                                    _plugin.GetInformation(_imageFormat, partitions[i].PartitionStartSector, partitions[i].PartitionStartSector + partitions[i].PartitionSectors - 1, out foo);
                                                    lstFs.Add(_plugin.XmlFSType);
                                                    Core.Statistics.AddFilesystem(_plugin.XmlFSType.Type);
                                                }
                                            }
                                            catch
                                            {
                                                //DicConsole.DebugWriteLine("Create-sidecar command", "Plugin {0} crashed", _plugin.Name);
                                            }
                                        }

                                        if (lstFs.Count > 0)
                                            xmlTrk.FileSystemInformation[i].FileSystems = lstFs.ToArray();
                                    }
                                }
                                else
                                {
                                    xmlTrk.FileSystemInformation[0] = new PartitionType();
                                    xmlTrk.FileSystemInformation[0].EndSector = (int)xmlTrk.EndSector;
                                    xmlTrk.FileSystemInformation[0].StartSector = (int)xmlTrk.StartSector;

                                    List<FileSystemType> lstFs = new List<FileSystemType>();

                                    foreach (Plugin _plugin in plugins.PluginsList.Values)
                                    {
                                        try
                                        {
                                            if (_plugin.Identify(_imageFormat, (ulong)xmlTrk.StartSector, (ulong)xmlTrk.EndSector))
                                            {
                                                string foo;
                                                _plugin.GetInformation(_imageFormat, (ulong)xmlTrk.StartSector, (ulong)xmlTrk.EndSector, out foo);
                                                lstFs.Add(_plugin.XmlFSType);
                                                Core.Statistics.AddFilesystem(_plugin.XmlFSType.Type);
                                            }
                                        }
                                        catch
                                        {
                                            //DicConsole.DebugWriteLine("Create-sidecar command", "Plugin {0} crashed", _plugin.Name);
                                        }
                                    }

                                    if (lstFs.Count > 0)
                                        xmlTrk.FileSystemInformation[0].FileSystems = lstFs.ToArray();
                                }

                                trksLst.Add(xmlTrk);
                            }

                            if (trksLst != null)
                                sidecar.OpticalDisc[0].Track = trksLst.ToArray();

                            break;
                        }
                    case XmlMediaType.BlockMedia:
                        {
                            sidecar.BlockMedia = new BlockMediaType[1];
                            sidecar.BlockMedia[0] = new BlockMediaType();
                            sidecar.BlockMedia[0].Checksums = imgChecksums.ToArray();
                            sidecar.BlockMedia[0].Image = new ImageType();
                            sidecar.BlockMedia[0].Image.format = _imageFormat.GetImageFormat();
                            sidecar.BlockMedia[0].Image.offset = 0;
                            sidecar.BlockMedia[0].Image.offsetSpecified = true;
                            sidecar.BlockMedia[0].Image.Value = Path.GetFileName(options.InputFile);
                            sidecar.BlockMedia[0].Size = fi.Length;
                            sidecar.BlockMedia[0].Sequence = new SequenceType();
                            if (_imageFormat.GetMediaSequence() != 0 && _imageFormat.GetLastDiskSequence() != 0)
                            {
                                sidecar.BlockMedia[0].Sequence.MediaSequence = _imageFormat.GetMediaSequence();
                                sidecar.BlockMedia[0].Sequence.TotalMedia = _imageFormat.GetMediaSequence();
                            }
                            else
                            {
                                sidecar.BlockMedia[0].Sequence.MediaSequence = 1;
                                sidecar.BlockMedia[0].Sequence.TotalMedia = 1;
                            }
                            sidecar.BlockMedia[0].Sequence.MediaTitle = _imageFormat.GetImageName();

                            foreach (MediaTagType tagType in _imageFormat.ImageInfo.readableMediaTags)
                            {
                                switch (tagType)
                                {
                                    case MediaTagType.ATAPI_IDENTIFY:
                                        sidecar.BlockMedia[0].ATA = new ATAType();
                                        sidecar.BlockMedia[0].ATA.Identify = new DumpType();
                                        sidecar.BlockMedia[0].ATA.Identify.Checksums = Core.Checksum.GetChecksums(_imageFormat.ReadDiskTag(MediaTagType.ATAPI_IDENTIFY)).ToArray();
                                        sidecar.BlockMedia[0].ATA.Identify.Size = _imageFormat.ReadDiskTag(MediaTagType.ATAPI_IDENTIFY).Length;
                                        break;
                                    case MediaTagType.ATA_IDENTIFY:
                                        sidecar.BlockMedia[0].ATA = new ATAType();
                                        sidecar.BlockMedia[0].ATA.Identify = new DumpType();
                                        sidecar.BlockMedia[0].ATA.Identify.Checksums = Core.Checksum.GetChecksums(_imageFormat.ReadDiskTag(MediaTagType.ATA_IDENTIFY)).ToArray();
                                        sidecar.BlockMedia[0].ATA.Identify.Size = _imageFormat.ReadDiskTag(MediaTagType.ATA_IDENTIFY).Length;
                                        break;
                                    case MediaTagType.PCMCIA_CIS:
                                        sidecar.BlockMedia[0].PCMCIA = new PCMCIAType();
                                        sidecar.BlockMedia[0].PCMCIA.CIS = new DumpType();
                                        sidecar.BlockMedia[0].PCMCIA.CIS.Checksums = Core.Checksum.GetChecksums(_imageFormat.ReadDiskTag(MediaTagType.PCMCIA_CIS)).ToArray();
                                        sidecar.BlockMedia[0].PCMCIA.CIS.Size = _imageFormat.ReadDiskTag(MediaTagType.PCMCIA_CIS).Length;
                                        break;
                                    case MediaTagType.SCSI_INQUIRY:
                                        sidecar.BlockMedia[0].SCSI = new SCSIType();
                                        sidecar.BlockMedia[0].SCSI.Inquiry = new DumpType();
                                        sidecar.BlockMedia[0].SCSI.Inquiry.Checksums = Core.Checksum.GetChecksums(_imageFormat.ReadDiskTag(MediaTagType.SCSI_INQUIRY)).ToArray();
                                        sidecar.BlockMedia[0].SCSI.Inquiry.Size = _imageFormat.ReadDiskTag(MediaTagType.SCSI_INQUIRY).Length;
                                        break;
                                    case MediaTagType.SD_CID:
                                        if(sidecar.BlockMedia[0].SecureDigital == null)
                                            sidecar.BlockMedia[0].SecureDigital = new SecureDigitalType();
                                        sidecar.BlockMedia[0].SecureDigital.CID = new DumpType();
                                        sidecar.BlockMedia[0].SecureDigital.CID.Checksums = Core.Checksum.GetChecksums(_imageFormat.ReadDiskTag(MediaTagType.SD_CID)).ToArray();
                                        sidecar.BlockMedia[0].SecureDigital.CID.Size = _imageFormat.ReadDiskTag(MediaTagType.SD_CID).Length;
                                        break;
                                    case MediaTagType.SD_CSD:
                                        if(sidecar.BlockMedia[0].SecureDigital == null)
                                            sidecar.BlockMedia[0].SecureDigital = new SecureDigitalType();
                                        sidecar.BlockMedia[0].SecureDigital.CSD = new DumpType();
                                        sidecar.BlockMedia[0].SecureDigital.CSD.Checksums = Core.Checksum.GetChecksums(_imageFormat.ReadDiskTag(MediaTagType.SD_CSD)).ToArray();
                                        sidecar.BlockMedia[0].SecureDigital.CSD.Size = _imageFormat.ReadDiskTag(MediaTagType.SD_CSD).Length;
                                        break;
                                    case MediaTagType.SD_ExtendedCSD:
                                        if(sidecar.BlockMedia[0].SecureDigital == null)
                                            sidecar.BlockMedia[0].SecureDigital = new SecureDigitalType();
                                        sidecar.BlockMedia[0].SecureDigital.ExtendedCSD = new DumpType();
                                        sidecar.BlockMedia[0].SecureDigital.ExtendedCSD.Checksums = Core.Checksum.GetChecksums(_imageFormat.ReadDiskTag(MediaTagType.SD_ExtendedCSD)).ToArray();
                                        sidecar.BlockMedia[0].SecureDigital.ExtendedCSD.Size = _imageFormat.ReadDiskTag(MediaTagType.SD_ExtendedCSD).Length;
                                        break;
                                }
                            }

                            string dskType, dskSubType;
                            Metadata.MediaType.MediaTypeToString(_imageFormat.ImageInfo.mediaType, out dskType, out dskSubType);
                            sidecar.BlockMedia[0].DiskType = dskType;
                            sidecar.BlockMedia[0].DiskSubType = dskSubType;
                            Core.Statistics.AddMedia(_imageFormat.ImageInfo.mediaType, false);

                            sidecar.BlockMedia[0].Dimensions = Metadata.Dimensions.DimensionsFromMediaType(_imageFormat.ImageInfo.mediaType);

                            sidecar.BlockMedia[0].LogicalBlocks = (long)_imageFormat.GetSectors();
                            sidecar.BlockMedia[0].LogicalBlockSize = (int)_imageFormat.GetSectorSize();
                            // TODO: Detect it
                            sidecar.BlockMedia[0].PhysicalBlockSize = (int)_imageFormat.GetSectorSize();

                            DicConsole.WriteLine("Checking filesystems...");

                            List<Partition> partitions = new List<Partition>();

                            foreach (PartPlugin _partplugin in plugins.PartPluginsList.Values)
                            {
                                List<Partition> _partitions;

                                if (_partplugin.GetInformation(_imageFormat, out _partitions))
                                {
                                    partitions = _partitions;
                                    Core.Statistics.AddPartition(_partplugin.Name);
                                    break;
                                }
                            }

                            sidecar.BlockMedia[0].FileSystemInformation = new PartitionType[1];
                            if (partitions.Count > 0)
                            {
                                sidecar.BlockMedia[0].FileSystemInformation = new PartitionType[partitions.Count];
                                for (int i = 0; i < partitions.Count; i++)
                                {
                                    sidecar.BlockMedia[0].FileSystemInformation[i] = new PartitionType();
                                    sidecar.BlockMedia[0].FileSystemInformation[i].Description = partitions[i].PartitionDescription;
                                    sidecar.BlockMedia[0].FileSystemInformation[i].EndSector = (int)(partitions[i].PartitionStartSector + partitions[i].PartitionSectors - 1);
                                    sidecar.BlockMedia[0].FileSystemInformation[i].Name = partitions[i].PartitionName;
                                    sidecar.BlockMedia[0].FileSystemInformation[i].Sequence = (int)partitions[i].PartitionSequence;
                                    sidecar.BlockMedia[0].FileSystemInformation[i].StartSector = (int)partitions[i].PartitionStartSector;
                                    sidecar.BlockMedia[0].FileSystemInformation[i].Type = partitions[i].PartitionType;

                                    List<FileSystemType> lstFs = new List<FileSystemType>();

                                    foreach (Plugin _plugin in plugins.PluginsList.Values)
                                    {
                                        try
                                        {
                                            if (_plugin.Identify(_imageFormat, partitions[i].PartitionStartSector, partitions[i].PartitionStartSector + partitions[i].PartitionSectors - 1))
                                            {
                                                string foo;
                                                _plugin.GetInformation(_imageFormat, partitions[i].PartitionStartSector, partitions[i].PartitionStartSector + partitions[i].PartitionSectors - 1, out foo);
                                                lstFs.Add(_plugin.XmlFSType);
                                                Core.Statistics.AddFilesystem(_plugin.XmlFSType.Type);
                                            }
                                        }
                                        catch
                                        {
                                            //DicConsole.DebugWriteLine("Create-sidecar command", "Plugin {0} crashed", _plugin.Name);
                                        }
                                    }

                                    if (lstFs.Count > 0)
                                        sidecar.BlockMedia[0].FileSystemInformation[i].FileSystems = lstFs.ToArray();
                                }
                            }
                            else
                            {
                                sidecar.BlockMedia[0].FileSystemInformation[0] = new PartitionType();
                                sidecar.BlockMedia[0].FileSystemInformation[0].StartSector = 0;
                                sidecar.BlockMedia[0].FileSystemInformation[0].EndSector = (int)(_imageFormat.GetSectors() - 1);

                                List<FileSystemType> lstFs = new List<FileSystemType>();

                                foreach (Plugin _plugin in plugins.PluginsList.Values)
                                {
                                    try
                                    {
                                        if (_plugin.Identify(_imageFormat, 0, _imageFormat.GetSectors() - 1))
                                        {
                                            string foo;
                                            _plugin.GetInformation(_imageFormat, 0, _imageFormat.GetSectors() - 1, out foo);
                                            lstFs.Add(_plugin.XmlFSType);
                                            Core.Statistics.AddFilesystem(_plugin.XmlFSType.Type);
                                        }
                                    }
                                    catch
                                    {
                                        //DicConsole.DebugWriteLine("Create-sidecar command", "Plugin {0} crashed", _plugin.Name);
                                    }
                                }

                                if (lstFs.Count > 0)
                                    sidecar.BlockMedia[0].FileSystemInformation[0].FileSystems = lstFs.ToArray();
                            }


                            // TODO: Implement support for getting CHS
                            break;
                        }
                    case XmlMediaType.LinearMedia:
                        {
                            sidecar.LinearMedia = new LinearMediaType[1];
                            sidecar.LinearMedia[0] = new LinearMediaType();
                            sidecar.LinearMedia[0].Checksums = imgChecksums.ToArray();
                            sidecar.LinearMedia[0].Image = new ImageType();
                            sidecar.LinearMedia[0].Image.format = _imageFormat.GetImageFormat();
                            sidecar.LinearMedia[0].Image.offset = 0;
                            sidecar.LinearMedia[0].Image.offsetSpecified = true;
                            sidecar.LinearMedia[0].Image.Value = Path.GetFileName(options.InputFile);
                            sidecar.LinearMedia[0].Size = fi.Length;

                            //MediaType dskType = _imageFormat.ImageInfo.diskType;
                            // TODO: Complete it
                            break;
                        }
                    case XmlMediaType.AudioMedia:
                        {
                            sidecar.AudioMedia = new AudioMediaType[1];
                            sidecar.AudioMedia[0] = new AudioMediaType();
                            sidecar.AudioMedia[0].Checksums = imgChecksums.ToArray();
                            sidecar.AudioMedia[0].Image = new ImageType();
                            sidecar.AudioMedia[0].Image.format = _imageFormat.GetImageFormat();
                            sidecar.AudioMedia[0].Image.offset = 0;
                            sidecar.AudioMedia[0].Image.offsetSpecified = true;
                            sidecar.AudioMedia[0].Image.Value = Path.GetFileName(options.InputFile);
                            sidecar.AudioMedia[0].Size = fi.Length;
                            sidecar.AudioMedia[0].Sequence = new SequenceType();
                            if (_imageFormat.GetMediaSequence() != 0 && _imageFormat.GetLastDiskSequence() != 0)
                            {
                                sidecar.AudioMedia[0].Sequence.MediaSequence = _imageFormat.GetMediaSequence();
                                sidecar.AudioMedia[0].Sequence.TotalMedia = _imageFormat.GetMediaSequence();
                            }
                            else
                            {
                                sidecar.AudioMedia[0].Sequence.MediaSequence = 1;
                                sidecar.AudioMedia[0].Sequence.TotalMedia = 1;
                            }
                            sidecar.AudioMedia[0].Sequence.MediaTitle = _imageFormat.GetImageName();

                            //MediaType dskType = _imageFormat.ImageInfo.diskType;
                            // TODO: Complete it
                            break;
                        }

                }

                DicConsole.WriteLine("Writing metadata sidecar");

                FileStream xmlFs = new FileStream(Path.GetDirectoryName(options.InputFile) +
                    //Path.PathSeparator +
                                   Path.GetFileNameWithoutExtension(options.InputFile) + ".cicm.xml",
                                       FileMode.CreateNew);

                System.Xml.Serialization.XmlSerializer xmlSer = new System.Xml.Serialization.XmlSerializer(typeof(CICMMetadataType));
                xmlSer.Serialize(xmlFs, sidecar);
                xmlFs.Close();

                Core.Statistics.AddCommand("create-sidecar");
            }
            catch (Exception ex)
            {
                DicConsole.ErrorWriteLine(String.Format("Error reading file: {0}", ex.Message));
                DicConsole.DebugWriteLine("Analyze command", ex.StackTrace);
            }

        }

        static string LbaToMsf(long lba)
        {
            long m, s, f;
            if (lba >= -150)
            {
                m = (lba + 150) / (75 * 60);
                lba -= m * (75 * 60);
                s = (lba + 150) / 75;
                lba -= s * 75;
                f = lba + 150;
            }
            else
            {
                m = (lba + 450150) / (75 * 60);
                lba -= m * (75 * 60);
                s = (lba + 450150) / 75;
                lba -= s * 75;
                f = lba + 450150;
            }

            return String.Format("{0}:{1:D2}:{2:D2}", m, s, f);
        }

        static string DdcdLbaToMsf(long lba)
        {
            long h, m, s, f;
            if (lba >= -150)
            {
                h = (lba + 150) / (75 * 60 * 60);
                lba -= h * (75 * 60 * 60);
                m = (lba + 150) / (75 * 60);
                lba -= m * (75 * 60);
                s = (lba + 150) / 75;
                lba -= s * 75;
                f = lba + 150;
            }
            else
            {
                h = (lba + 450150 * 2) / (75 * 60 * 60);
                lba -= h * (75 * 60 * 60);
                m = (lba + 450150 * 2) / (75 * 60);
                lba -= m * (75 * 60);
                s = (lba + 450150 * 2) / 75;
                lba -= s * 75;
                f = lba + 450150 * 2;
            }

            return String.Format("{3}:{0:D2}:{1:D2}:{2:D2}", m, s, f, h);
        }
    }
}
