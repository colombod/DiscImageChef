﻿// /***************************************************************************
// The Disc Image Chef
// ----------------------------------------------------------------------------
//
// Filename       : SBC.cs
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
using System.Collections.Generic;
using System.IO;
using DiscImageChef.CommonTypes;
using DiscImageChef.Console;
using DiscImageChef.Core.Logging;
using DiscImageChef.Devices;
using DiscImageChef.Filesystems;
using DiscImageChef.Filters;
using DiscImageChef.ImagePlugins;
using DiscImageChef.PartPlugins;
using Schemas;

namespace DiscImageChef.Core.Devices.Dumping
{
    internal static class SBC
    {
        internal static void Dump(Device dev, string devicePath, string outputPrefix, ushort retryPasses, bool force, bool dumpRaw, bool persistent, bool stopOnError, ref CICMMetadataType sidecar, ref MediaType dskType, bool opticalDisc)
        {
            MHDDLog mhddLog;
            IBGLog ibgLog;
            byte[] cmdBuf = null;
            byte[] senseBuf = null;
            bool sense = false;
            double duration;
            ulong blocks = 0;
            uint blockSize = 0;
            uint logicalBlockSize = 0;
            uint physicalBlockSize = 0;
            byte scsiMediumType = 0;
            byte scsiDensityCode = 0;
            bool containsFloppyPage = false;
            ushort currentProfile = 0x0001;
            DateTime start;
            DateTime end;
            double totalDuration = 0;
            double totalChkDuration = 0;
            double currentSpeed = 0;
            double maxSpeed = double.MinValue;
            double minSpeed = double.MaxValue;
            List<ulong> unreadableSectors = new List<ulong>();
            Checksum dataChk;
            byte[] readBuffer;
            uint blocksToRead = 64;
            ulong errored = 0;
            DataFile dumpFile = null;
            bool aborted = false;
            System.Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = aborted = true;
            };

            Reader scsiReader = new Reader(dev, dev.Timeout, null, dumpRaw);
            blocks = scsiReader.GetDeviceBlocks();
            blockSize = scsiReader.LogicalBlockSize;
            if(scsiReader.FindReadCommand())
            {
                DicConsole.ErrorWriteLine("Unable to read medium.");
                return;
            }

            if(blocks != 0 && blockSize != 0)
            {
                blocks++;
                DicConsole.WriteLine("Media has {0} blocks of {1} bytes/each. (for a total of {2} bytes)",
                    blocks, blockSize, blocks * (ulong)blockSize);
            }
            blocksToRead = scsiReader.BlocksToRead;
            logicalBlockSize = blockSize;
            physicalBlockSize = scsiReader.PhysicalBlockSize;

            if(blocks == 0)
            {
                DicConsole.ErrorWriteLine("Unable to read medium or empty medium present...");
                return;
            }

            if(dskType == MediaType.Unknown)
                dskType = MediaTypeFromSCSI.Get((byte)dev.SCSIType, dev.Manufacturer, dev.Model, scsiMediumType, scsiDensityCode, blocks, blockSize);

            if(dskType == MediaType.Unknown && dev.IsUSB && containsFloppyPage)
                dskType = MediaType.FlashDrive;

            DicConsole.WriteLine("Media identified as {0}", dskType);

            if(!opticalDisc)
            {
                sidecar.BlockMedia = new BlockMediaType[1];
                sidecar.BlockMedia[0] = new BlockMediaType();

                // All USB flash drives report as removable, even if the media is not removable
                if(!dev.IsRemovable || dev.IsUSB)
                {
                    if(dev.IsUSB)
                    {
                        sidecar.BlockMedia[0].USB = new USBType();
                        sidecar.BlockMedia[0].USB.ProductID = dev.USBProductID;
                        sidecar.BlockMedia[0].USB.VendorID = dev.USBVendorID;
                        sidecar.BlockMedia[0].USB.Descriptors = new DumpType();
                        sidecar.BlockMedia[0].USB.Descriptors.Image = outputPrefix + ".usbdescriptors.bin";
                        sidecar.BlockMedia[0].USB.Descriptors.Size = dev.USBDescriptors.Length;
                        sidecar.BlockMedia[0].USB.Descriptors.Checksums = Checksum.GetChecksums(dev.USBDescriptors).ToArray();
                        DataFile.WriteTo("SCSI Dump", sidecar.BlockMedia[0].USB.Descriptors.Image, dev.USBDescriptors);
                    }

                    if(dev.Type == DeviceType.ATAPI)
                    {
                        Decoders.ATA.AtaErrorRegistersCHS errorRegs;
                        sense = dev.AtapiIdentify(out cmdBuf, out errorRegs);
                        if(!sense)
                        {
                            sidecar.BlockMedia[0].ATA = new ATAType();
                            sidecar.BlockMedia[0].ATA.Identify = new DumpType();
                            sidecar.BlockMedia[0].ATA.Identify.Image = outputPrefix + ".identify.bin";
                            sidecar.BlockMedia[0].ATA.Identify.Size = cmdBuf.Length;
                            sidecar.BlockMedia[0].ATA.Identify.Checksums = Checksum.GetChecksums(cmdBuf).ToArray();
                            DataFile.WriteTo("SCSI Dump", sidecar.BlockMedia[0].ATA.Identify.Image, cmdBuf);
                        }
                    }

                    sense = dev.ScsiInquiry(out cmdBuf, out senseBuf);
                    if(!sense)
                    {
                        sidecar.BlockMedia[0].SCSI = new SCSIType();
                        sidecar.BlockMedia[0].SCSI.Inquiry = new DumpType();
                        sidecar.BlockMedia[0].SCSI.Inquiry.Image = outputPrefix + ".inquiry.bin";
                        sidecar.BlockMedia[0].SCSI.Inquiry.Size = cmdBuf.Length;
                        sidecar.BlockMedia[0].SCSI.Inquiry.Checksums = Checksum.GetChecksums(cmdBuf).ToArray();
                        DataFile.WriteTo("SCSI Dump", sidecar.BlockMedia[0].SCSI.Inquiry.Image, cmdBuf);

                        sense = dev.ScsiInquiry(out cmdBuf, out senseBuf, 0x00);
                        if(!sense)
                        {
                            byte[] pages = Decoders.SCSI.EVPD.DecodePage00(cmdBuf);

                            if(pages != null)
                            {
                                List<EVPDType> evpds = new List<EVPDType>();
                                foreach(byte page in pages)
                                {
                                    sense = dev.ScsiInquiry(out cmdBuf, out senseBuf, page);
                                    if(!sense)
                                    {
                                        EVPDType evpd = new EVPDType();
                                        evpd.Image = string.Format("{0}.evpd_{1:X2}h.bin", outputPrefix, page);
                                        evpd.Checksums = Checksum.GetChecksums(cmdBuf).ToArray();
                                        evpd.Size = cmdBuf.Length;
                                        evpd.Checksums = Checksum.GetChecksums(cmdBuf).ToArray();
                                        DataFile.WriteTo("SCSI Dump", evpd.Image, cmdBuf);
                                        evpds.Add(evpd);
                                    }
                                }

                                if(evpds.Count > 0)
                                    sidecar.BlockMedia[0].SCSI.EVPD = evpds.ToArray();
                            }
                        }

                        sense = dev.ModeSense10(out cmdBuf, out senseBuf, false, true, ScsiModeSensePageControl.Current, 0x3F, 0xFF, 5, out duration);
                        if(!sense || dev.Error)
                        {
                            sense = dev.ModeSense10(out cmdBuf, out senseBuf, false, true, ScsiModeSensePageControl.Current, 0x3F, 0x00, 5, out duration);
                        }

                        Decoders.SCSI.Modes.DecodedMode? decMode = null;

                        if(!sense && !dev.Error)
                        {
                            if(Decoders.SCSI.Modes.DecodeMode10(cmdBuf, dev.SCSIType).HasValue)
                            {
                                decMode = Decoders.SCSI.Modes.DecodeMode10(cmdBuf, dev.SCSIType);
                                sidecar.BlockMedia[0].SCSI.ModeSense10 = new DumpType();
                                sidecar.BlockMedia[0].SCSI.ModeSense10.Image = outputPrefix + ".modesense10.bin";
                                sidecar.BlockMedia[0].SCSI.ModeSense10.Size = cmdBuf.Length;
                                sidecar.BlockMedia[0].SCSI.ModeSense10.Checksums = Checksum.GetChecksums(cmdBuf).ToArray();
                                DataFile.WriteTo("SCSI Dump", sidecar.BlockMedia[0].SCSI.ModeSense10.Image, cmdBuf);
                            }
                        }

                        sense = dev.ModeSense6(out cmdBuf, out senseBuf, false, ScsiModeSensePageControl.Current, 0x3F, 0x00, 5, out duration);
                        if(sense || dev.Error)
                            sense = dev.ModeSense6(out cmdBuf, out senseBuf, false, ScsiModeSensePageControl.Current, 0x3F, 0x00, 5, out duration);
                        if(sense || dev.Error)
                            sense = dev.ModeSense(out cmdBuf, out senseBuf, 5, out duration);

                        if(!sense && !dev.Error)
                        {
                            if(Decoders.SCSI.Modes.DecodeMode6(cmdBuf, dev.SCSIType).HasValue)
                            {
                                decMode = Decoders.SCSI.Modes.DecodeMode6(cmdBuf, dev.SCSIType);
                                sidecar.BlockMedia[0].SCSI.ModeSense = new DumpType();
                                sidecar.BlockMedia[0].SCSI.ModeSense.Image = outputPrefix + ".modesense.bin";
                                sidecar.BlockMedia[0].SCSI.ModeSense.Size = cmdBuf.Length;
                                sidecar.BlockMedia[0].SCSI.ModeSense.Checksums = Checksum.GetChecksums(cmdBuf).ToArray();
                                DataFile.WriteTo("SCSI Dump", sidecar.BlockMedia[0].SCSI.ModeSense.Image, cmdBuf);
                            }
                        }

                        if(decMode.HasValue)
                        {
                            scsiMediumType = (byte)decMode.Value.Header.MediumType;
                            if(decMode.Value.Header.BlockDescriptors != null && decMode.Value.Header.BlockDescriptors.Length >= 1)
                                scsiDensityCode = (byte)decMode.Value.Header.BlockDescriptors[0].Density;

                            foreach(Decoders.SCSI.Modes.ModePage modePage in decMode.Value.Pages)
                                containsFloppyPage |= modePage.Page == 0x05;
                        }
                    }
                }
            }

            uint longBlockSize = scsiReader.LongBlockSize;

            if(dumpRaw)
            {
                if(blockSize == longBlockSize)
                {
                    if(!scsiReader.CanReadRaw)
                    {
                        DicConsole.ErrorWriteLine("Device doesn't seem capable of reading raw data from media.");
                    }
                    else
                    {
                        DicConsole.ErrorWriteLine("Device is capable of reading raw data but I've been unable to guess correct sector size.");
                    }

                    if(!force)
                    {
                        DicConsole.ErrorWriteLine("Not continuing. If you want to continue reading cooked data when raw is not available use the force option.");
                        // TODO: Exit more gracefully
                        return;
                    }

                    DicConsole.ErrorWriteLine("Continuing dumping cooked data.");
                    dumpRaw = false;
                }
                else
                {

                    if(longBlockSize == 37856) // Only a block will be read, but it contains 16 sectors and command expect sector number not block number
                        blocksToRead = 16;
                    else
                        blocksToRead = 1;
                    DicConsole.WriteLine("Reading {0} raw bytes ({1} cooked bytes) per sector.",
                                         longBlockSize, blockSize * blocksToRead);
                    physicalBlockSize = longBlockSize;
                    blockSize = longBlockSize;
                }

            }
            DicConsole.WriteLine("Reading {0} sectors at a time.", blocksToRead);

            string outputExtension = ".bin";
            if(opticalDisc && blockSize == 2048)
                outputExtension = ".iso";
            mhddLog = new MHDDLog(outputPrefix + ".mhddlog.bin", dev, blocks, blockSize, blocksToRead);
            ibgLog = new IBGLog(outputPrefix + ".ibg", currentProfile);
            dumpFile = new DataFile(outputPrefix + outputExtension);

            start = DateTime.UtcNow;

            readBuffer = null;

            for(ulong i = 0; i < blocks; i += blocksToRead)
            {
                if(aborted)
                    break;

                double cmdDuration = 0;

                if((blocks - i) < blocksToRead)
                    blocksToRead = (uint)(blocks - i);

#pragma warning disable RECS0018 // Comparison of floating point numbers with equality operator
                if(currentSpeed > maxSpeed && currentSpeed != 0)
                    maxSpeed = currentSpeed;
                if(currentSpeed < minSpeed && currentSpeed != 0)
                    minSpeed = currentSpeed;
#pragma warning restore RECS0018 // Comparison of floating point numbers with equality operator

                DicConsole.Write("\rReading sector {0} of {1} ({2:F3} MiB/sec.)", i, blocks, currentSpeed);

                sense = scsiReader.ReadBlocks(out readBuffer, i, blocksToRead, out cmdDuration);
                totalDuration += cmdDuration;

                if(!sense && !dev.Error)
                {
                    mhddLog.Write(i, cmdDuration);
                    ibgLog.Write(i, currentSpeed * 1024);
                    dumpFile.Write(readBuffer);
                }
                else
                {
                    // TODO: Reset device after X errors
                    if(stopOnError)
                        return; // TODO: Return more cleanly

                    // Write empty data
                    dumpFile.Write(new byte[blockSize * blocksToRead]);

                    // TODO: Record error on mapfile

                    errored += blocksToRead;
                    unreadableSectors.Add(i);
                    if(cmdDuration < 500)
                        mhddLog.Write(i, 65535);
                    else
                        mhddLog.Write(i, cmdDuration);

                    ibgLog.Write(i, 0);
                }

#pragma warning disable IDE0004 // Remove Unnecessary Cast
                currentSpeed = ((double)blockSize * blocksToRead / (double)1048576) / (cmdDuration / (double)1000);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
            }
            end = DateTime.UtcNow;
            DicConsole.WriteLine();
            mhddLog.Close();
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            ibgLog.Close(dev, blocks, blockSize, (end - start).TotalSeconds, currentSpeed * 1024, (((double)blockSize * (double)(blocks + 1)) / 1024) / (totalDuration / 1000), devicePath);
#pragma warning restore IDE0004 // Remove Unnecessary Cast

            #region Error handling
            if(unreadableSectors.Count > 0 && !aborted)
            {
                List<ulong> tmpList = new List<ulong>();

                foreach(ulong ur in unreadableSectors)
                {
                    for(ulong i = ur; i < ur + blocksToRead; i++)
                        tmpList.Add(i);
                }

                tmpList.Sort();

                int pass = 0;
                bool forward = true;
                bool runningPersistent = false;

                unreadableSectors = tmpList;

            repeatRetry:
                ulong[] tmpArray = unreadableSectors.ToArray();
                foreach(ulong badSector in tmpArray)
                {
                    if(aborted)
                        break;

                    double cmdDuration = 0;

                    DicConsole.Write("\rRetrying sector {0}, pass {1}, {3}{2}", badSector, pass + 1, forward ? "forward" : "reverse", runningPersistent ? "recovering partial data, " : "");

                    sense = scsiReader.ReadBlock(out readBuffer, badSector, out cmdDuration);
                    totalDuration += cmdDuration;

                    if(!sense && !dev.Error)
                    {
                        unreadableSectors.Remove(badSector);
                        dumpFile.WriteAt(readBuffer, badSector, blockSize);
                    }
                    else if(runningPersistent)
                        dumpFile.WriteAt(readBuffer, badSector, blockSize);
                }

                if(pass < retryPasses && !aborted && unreadableSectors.Count > 0)
                {
                    pass++;
                    forward = !forward;
                    unreadableSectors.Sort();
                    unreadableSectors.Reverse();
                    goto repeatRetry;
                }

                Decoders.SCSI.Modes.DecodedMode? currentMode = null;
                Decoders.SCSI.Modes.ModePage? currentModePage = null;
                byte[] md6 = null;
                byte[] md10 = null;

                if(!runningPersistent && persistent)
                {
                    sense = dev.ModeSense6(out readBuffer, out senseBuf, false, ScsiModeSensePageControl.Current, 0x01, dev.Timeout, out duration);
                    if(sense)
                    {
                        sense = dev.ModeSense10(out readBuffer, out senseBuf, false, ScsiModeSensePageControl.Current, 0x01, dev.Timeout, out duration);
                        if(!sense)
                            currentMode = Decoders.SCSI.Modes.DecodeMode10(readBuffer, dev.SCSIType);
                    }
                    else
                        currentMode = Decoders.SCSI.Modes.DecodeMode6(readBuffer, dev.SCSIType);

                    if(currentMode.HasValue)
                        currentModePage = currentMode.Value.Pages[0];

                    if(dev.SCSIType == Decoders.SCSI.PeripheralDeviceTypes.MultiMediaDevice)
                    {
                        Decoders.SCSI.Modes.ModePage_01_MMC pgMMC = new Decoders.SCSI.Modes.ModePage_01_MMC();
                        pgMMC.PS = false;
                        pgMMC.ReadRetryCount = 255;
                        pgMMC.Parameter = 0x20;

                        Decoders.SCSI.Modes.DecodedMode md = new Decoders.SCSI.Modes.DecodedMode();
                        md.Header = new Decoders.SCSI.Modes.ModeHeader();
                        md.Pages = new Decoders.SCSI.Modes.ModePage[1];
                        md.Pages[0] = new Decoders.SCSI.Modes.ModePage();
                        md.Pages[0].Page = 0x01;
                        md.Pages[0].Subpage = 0x00;
                        md.Pages[0].PageResponse = Decoders.SCSI.Modes.EncodeModePage_01_MMC(pgMMC);
                        md6 = Decoders.SCSI.Modes.EncodeMode6(md, dev.SCSIType);
                        md10 = Decoders.SCSI.Modes.EncodeMode10(md, dev.SCSIType);
                    }
                    else
                    {
                        Decoders.SCSI.Modes.ModePage_01 pg = new Decoders.SCSI.Modes.ModePage_01();
                        pg.PS = false;
                        pg.AWRE = false;
                        pg.ARRE = false;
                        pg.TB = true;
                        pg.RC = false;
                        pg.EER = true;
                        pg.PER = false;
                        pg.DTE = false;
                        pg.DCR = false;
                        pg.ReadRetryCount = 255;

                        Decoders.SCSI.Modes.DecodedMode md = new Decoders.SCSI.Modes.DecodedMode();
                        md.Header = new Decoders.SCSI.Modes.ModeHeader();
                        md.Pages = new Decoders.SCSI.Modes.ModePage[1];
                        md.Pages[0] = new Decoders.SCSI.Modes.ModePage();
                        md.Pages[0].Page = 0x01;
                        md.Pages[0].Subpage = 0x00;
                        md.Pages[0].PageResponse = Decoders.SCSI.Modes.EncodeModePage_01(pg);
                        md6 = Decoders.SCSI.Modes.EncodeMode6(md, dev.SCSIType);
                        md10 = Decoders.SCSI.Modes.EncodeMode10(md, dev.SCSIType);
                    }

                    sense = dev.ModeSelect(md6, out senseBuf, true, false, dev.Timeout, out duration);
                    if(sense)
                    {
                        sense = dev.ModeSelect10(md10, out senseBuf, true, false, dev.Timeout, out duration);
                    }

                    runningPersistent = true;
                    if(!sense && !dev.Error)
                    {
                        pass--;
                        goto repeatRetry;
                    }
                }
                else if(runningPersistent && persistent && currentModePage.HasValue)
                {
                    Decoders.SCSI.Modes.DecodedMode md = new Decoders.SCSI.Modes.DecodedMode();
                    md.Header = new Decoders.SCSI.Modes.ModeHeader();
                    md.Pages = new Decoders.SCSI.Modes.ModePage[1];
                    md.Pages[0] = currentModePage.Value;
                    md6 = Decoders.SCSI.Modes.EncodeMode6(md, dev.SCSIType);
                    md10 = Decoders.SCSI.Modes.EncodeMode10(md, dev.SCSIType);

                    sense = dev.ModeSelect(md6, out senseBuf, true, false, dev.Timeout, out duration);
                    if(sense)
                    {
                        sense = dev.ModeSelect10(md10, out senseBuf, true, false, dev.Timeout, out duration);
                    }
                }

                DicConsole.WriteLine();
            }
            #endregion Error handling

            dataChk = new Checksum();
            dumpFile.Seek(0, SeekOrigin.Begin);
            blocksToRead = 500;

            for(ulong i = 0; i < blocks; i += blocksToRead)
            {
                if(aborted)
                    break;

                if((blocks - i) < blocksToRead)
                    blocksToRead = (uint)(blocks - i);

                DicConsole.Write("\rChecksumming sector {0} of {1} ({2:F3} MiB/sec.)", i, blocks, currentSpeed);

                DateTime chkStart = DateTime.UtcNow;
                byte[] dataToCheck = new byte[blockSize * blocksToRead];
                dumpFile.Read(dataToCheck, 0, (int)(blockSize * blocksToRead));
                dataChk.Update(dataToCheck);
                DateTime chkEnd = DateTime.UtcNow;

                double chkDuration = (chkEnd - chkStart).TotalMilliseconds;
                totalChkDuration += chkDuration;

#pragma warning disable IDE0004 // Cast is necessary, otherwise incorrect value is created
                currentSpeed = ((double)blockSize * blocksToRead / (double)1048576) / (chkDuration / (double)1000);
#pragma warning restore IDE0004 // Cast is necessary, otherwise incorrect value is created
            }
            DicConsole.WriteLine();
            dumpFile.Close();
            end = DateTime.UtcNow;

            PluginBase plugins = new PluginBase();
            plugins.RegisterAllPlugins();
            ImagePlugin _imageFormat;
            FiltersList filtersList = new FiltersList();
            Filter inputFilter = filtersList.GetFilter(outputPrefix + outputExtension);

            if(inputFilter == null)
            {
                DicConsole.ErrorWriteLine("Cannot open file just created, this should not happen.");
                return;
            }

            _imageFormat = ImageFormat.Detect(inputFilter);
            PartitionType[] xmlFileSysInfo = null;

            try
            {
                if(!_imageFormat.OpenImage(inputFilter))
                    _imageFormat = null;
            }
            catch
            {
                _imageFormat = null;
            }

            if(_imageFormat != null)
            {
                List<Partition> partitions = new List<Partition>();

                foreach(PartPlugin _partplugin in plugins.PartPluginsList.Values)
                {
                    List<Partition> _partitions;

                    if(_partplugin.GetInformation(_imageFormat, out _partitions))
                    {
                        partitions.AddRange(_partitions);
                        Statistics.AddPartition(_partplugin.Name);
                    }
                }

                if(partitions.Count > 0)
                {
                    xmlFileSysInfo = new PartitionType[partitions.Count];
                    for(int i = 0; i < partitions.Count; i++)
                    {
                        xmlFileSysInfo[i] = new PartitionType();
                        xmlFileSysInfo[i].Description = partitions[i].PartitionDescription;
                        xmlFileSysInfo[i].EndSector = (int)(partitions[i].PartitionStartSector + partitions[i].PartitionSectors - 1);
                        xmlFileSysInfo[i].Name = partitions[i].PartitionName;
                        xmlFileSysInfo[i].Sequence = (int)partitions[i].PartitionSequence;
                        xmlFileSysInfo[i].StartSector = (int)partitions[i].PartitionStartSector;
                        xmlFileSysInfo[i].Type = partitions[i].PartitionType;

                        List<FileSystemType> lstFs = new List<FileSystemType>();

                        foreach(Filesystem _plugin in plugins.PluginsList.Values)
                        {
                            try
                            {
                                if(_plugin.Identify(_imageFormat, partitions[i].PartitionStartSector, partitions[i].PartitionStartSector + partitions[i].PartitionSectors - 1))
                                {
                                    string foo;
                                    _plugin.GetInformation(_imageFormat, partitions[i].PartitionStartSector, partitions[i].PartitionStartSector + partitions[i].PartitionSectors - 1, out foo);
                                    lstFs.Add(_plugin.XmlFSType);
                                    Statistics.AddFilesystem(_plugin.XmlFSType.Type);

                                    if(_plugin.XmlFSType.Type == "Opera")
                                        dskType = MediaType.ThreeDO;
                                    if(_plugin.XmlFSType.Type == "PC Engine filesystem")
                                        dskType = MediaType.SuperCDROM2;
                                    if(_plugin.XmlFSType.Type == "Nintendo Wii filesystem")
                                        dskType = MediaType.WOD;
                                    if(_plugin.XmlFSType.Type == "Nintendo Gamecube filesystem")
                                        dskType = MediaType.GOD;
                                }
                            }
#pragma warning disable RECS0022 // A catch clause that catches System.Exception and has an empty body
                            catch
#pragma warning restore RECS0022 // A catch clause that catches System.Exception and has an empty body
                            {
                                //DicConsole.DebugWriteLine("Dump-media command", "Plugin {0} crashed", _plugin.Name);
                            }
                        }

                        if(lstFs.Count > 0)
                            xmlFileSysInfo[i].FileSystems = lstFs.ToArray();
                    }
                }
                else
                {
                    xmlFileSysInfo = new PartitionType[1];
                    xmlFileSysInfo[0] = new PartitionType();
                    xmlFileSysInfo[0].EndSector = (int)(blocks - 1);
                    xmlFileSysInfo[0].StartSector = 0;

                    List<FileSystemType> lstFs = new List<FileSystemType>();

                    foreach(Filesystem _plugin in plugins.PluginsList.Values)
                    {
                        try
                        {
                            if(_plugin.Identify(_imageFormat, (blocks - 1), 0))
                            {
                                string foo;
                                _plugin.GetInformation(_imageFormat, (blocks - 1), 0, out foo);
                                lstFs.Add(_plugin.XmlFSType);
                                Statistics.AddFilesystem(_plugin.XmlFSType.Type);

                                if(_plugin.XmlFSType.Type == "Opera")
                                    dskType = MediaType.ThreeDO;
                                if(_plugin.XmlFSType.Type == "PC Engine filesystem")
                                    dskType = MediaType.SuperCDROM2;
                                if(_plugin.XmlFSType.Type == "Nintendo Wii filesystem")
                                    dskType = MediaType.WOD;
                                if(_plugin.XmlFSType.Type == "Nintendo Gamecube filesystem")
                                    dskType = MediaType.GOD;
                            }
                        }
#pragma warning disable RECS0022 // A catch clause that catches System.Exception and has an empty body
                        catch
#pragma warning restore RECS0022 // A catch clause that catches System.Exception and has an empty body
                        {
                            //DicConsole.DebugWriteLine("Create-sidecar command", "Plugin {0} crashed", _plugin.Name);
                        }
                    }

                    if(lstFs.Count > 0)
                        xmlFileSysInfo[0].FileSystems = lstFs.ToArray();
                }
            }

            if(opticalDisc)
            {
                sidecar.OpticalDisc[0].Checksums = dataChk.End().ToArray();
                sidecar.OpticalDisc[0].DumpHardwareArray = new DumpHardwareType[1];
                sidecar.OpticalDisc[0].DumpHardwareArray[0] = new DumpHardwareType();
                sidecar.OpticalDisc[0].DumpHardwareArray[0].Extents = new ExtentType[1];
                sidecar.OpticalDisc[0].DumpHardwareArray[0].Extents[0] = new ExtentType();
                sidecar.OpticalDisc[0].DumpHardwareArray[0].Extents[0].Start = 0;
                sidecar.OpticalDisc[0].DumpHardwareArray[0].Extents[0].End = (int)(blocks - 1);
                sidecar.OpticalDisc[0].DumpHardwareArray[0].Manufacturer = dev.Manufacturer;
                sidecar.OpticalDisc[0].DumpHardwareArray[0].Model = dev.Model;
                sidecar.OpticalDisc[0].DumpHardwareArray[0].Revision = dev.Revision;
                sidecar.OpticalDisc[0].DumpHardwareArray[0].Software = Version.GetSoftwareType(dev.PlatformID);
                sidecar.OpticalDisc[0].Image = new ImageType();
                sidecar.OpticalDisc[0].Image.format = "Raw disk image (sector by sector copy)";
                sidecar.OpticalDisc[0].Image.Value = outputPrefix + outputExtension;
                // TODO: Implement layers
                //sidecar.OpticalDisc[0].Layers = new LayersType();
                sidecar.OpticalDisc[0].Sessions = 1;
                sidecar.OpticalDisc[0].Tracks = new[] { 1 };
                sidecar.OpticalDisc[0].Track = new Schemas.TrackType[1];
                sidecar.OpticalDisc[0].Track[0] = new Schemas.TrackType();
                sidecar.OpticalDisc[0].Track[0].BytesPerSector = (int)blockSize;
                sidecar.OpticalDisc[0].Track[0].Checksums = sidecar.OpticalDisc[0].Checksums;
                sidecar.OpticalDisc[0].Track[0].EndSector = (long)(blocks - 1);
                sidecar.OpticalDisc[0].Track[0].Image = new ImageType();
                sidecar.OpticalDisc[0].Track[0].Image.format = "BINARY";
                sidecar.OpticalDisc[0].Track[0].Image.offset = 0;
                sidecar.OpticalDisc[0].Track[0].Image.offsetSpecified = true;
                sidecar.OpticalDisc[0].Track[0].Image.Value = sidecar.OpticalDisc[0].Image.Value;
                sidecar.OpticalDisc[0].Track[0].Sequence = new TrackSequenceType();
                sidecar.OpticalDisc[0].Track[0].Sequence.Session = 1;
                sidecar.OpticalDisc[0].Track[0].Sequence.TrackNumber = 1;
                sidecar.OpticalDisc[0].Track[0].Size = (long)(blocks * blockSize);
                sidecar.OpticalDisc[0].Track[0].StartSector = 0;
                if(xmlFileSysInfo != null)
                    sidecar.OpticalDisc[0].Track[0].FileSystemInformation = xmlFileSysInfo;
                switch(dskType)
                {
                    case MediaType.DDCD:
                    case MediaType.DDCDR:
                    case MediaType.DDCDRW:
                        sidecar.OpticalDisc[0].Track[0].TrackType1 = TrackTypeTrackType.ddcd;
                        break;
                    case MediaType.DVDROM:
                    case MediaType.DVDR:
                    case MediaType.DVDRAM:
                    case MediaType.DVDRW:
                    case MediaType.DVDRDL:
                    case MediaType.DVDRWDL:
                    case MediaType.DVDDownload:
                    case MediaType.DVDPRW:
                    case MediaType.DVDPR:
                    case MediaType.DVDPRWDL:
                    case MediaType.DVDPRDL:
                        sidecar.OpticalDisc[0].Track[0].TrackType1 = TrackTypeTrackType.dvd;
                        break;
                    case MediaType.HDDVDROM:
                    case MediaType.HDDVDR:
                    case MediaType.HDDVDRAM:
                    case MediaType.HDDVDRW:
                    case MediaType.HDDVDRDL:
                    case MediaType.HDDVDRWDL:
                        sidecar.OpticalDisc[0].Track[0].TrackType1 = TrackTypeTrackType.hddvd;
                        break;
                    case MediaType.BDROM:
                    case MediaType.BDR:
                    case MediaType.BDRE:
                    case MediaType.BDREXL:
                    case MediaType.BDRXL:
                        sidecar.OpticalDisc[0].Track[0].TrackType1 = TrackTypeTrackType.bluray;
                        break;
                }
                sidecar.OpticalDisc[0].Dimensions = Metadata.Dimensions.DimensionsFromMediaType(dskType);
                string xmlDskTyp, xmlDskSubTyp;
                Metadata.MediaType.MediaTypeToString(dskType, out xmlDskTyp, out xmlDskSubTyp);
                sidecar.OpticalDisc[0].DiscType = xmlDskTyp;
                sidecar.OpticalDisc[0].DiscSubType = xmlDskSubTyp;
            }
            else
            {
                sidecar.BlockMedia[0].Checksums = dataChk.End().ToArray();
                sidecar.BlockMedia[0].Dimensions = Metadata.Dimensions.DimensionsFromMediaType(dskType);
                string xmlDskTyp, xmlDskSubTyp;
                Metadata.MediaType.MediaTypeToString(dskType, out xmlDskTyp, out xmlDskSubTyp);
                sidecar.BlockMedia[0].DiskType = xmlDskTyp;
                sidecar.BlockMedia[0].DiskSubType = xmlDskSubTyp;
                // TODO: Implement device firmware revision
                sidecar.BlockMedia[0].Image = new ImageType();
                sidecar.BlockMedia[0].Image.format = "Raw disk image (sector by sector copy)";
                sidecar.BlockMedia[0].Image.Value = outputPrefix + ".bin";
                if(!dev.IsRemovable || dev.IsUSB)
                {
                    if(dev.Type == DeviceType.ATAPI)
                        sidecar.BlockMedia[0].Interface = "ATAPI";
                    else if(dev.IsUSB)
                        sidecar.BlockMedia[0].Interface = "USB";
                    else if(dev.IsFireWire)
                        sidecar.BlockMedia[0].Interface = "FireWire";
                    else
                        sidecar.BlockMedia[0].Interface = "SCSI";
                }
                sidecar.BlockMedia[0].LogicalBlocks = (long)blocks;
                sidecar.BlockMedia[0].PhysicalBlockSize = (int)physicalBlockSize;
                sidecar.BlockMedia[0].LogicalBlockSize = (int)logicalBlockSize;
                sidecar.BlockMedia[0].Manufacturer = dev.Manufacturer;
                sidecar.BlockMedia[0].Model = dev.Model;
                sidecar.BlockMedia[0].Serial = dev.Serial;
                sidecar.BlockMedia[0].Size = (long)(blocks * blockSize);
                if(xmlFileSysInfo != null)
                    sidecar.BlockMedia[0].FileSystemInformation = xmlFileSysInfo;

                if(dev.IsRemovable)
                {
                    sidecar.BlockMedia[0].DumpHardwareArray = new DumpHardwareType[1];
                    sidecar.BlockMedia[0].DumpHardwareArray[0] = new DumpHardwareType();
                    sidecar.BlockMedia[0].DumpHardwareArray[0].Extents = new ExtentType[1];
                    sidecar.BlockMedia[0].DumpHardwareArray[0].Extents[0] = new ExtentType();
                    sidecar.BlockMedia[0].DumpHardwareArray[0].Extents[0].Start = 0;
                    sidecar.BlockMedia[0].DumpHardwareArray[0].Extents[0].End = (int)(blocks - 1);
                    sidecar.BlockMedia[0].DumpHardwareArray[0].Manufacturer = dev.Manufacturer;
                    sidecar.BlockMedia[0].DumpHardwareArray[0].Model = dev.Model;
                    sidecar.BlockMedia[0].DumpHardwareArray[0].Revision = dev.Revision;
                    sidecar.BlockMedia[0].DumpHardwareArray[0].Software = Version.GetSoftwareType(dev.PlatformID);
                }
            }

            DicConsole.WriteLine();

            DicConsole.WriteLine("Took a total of {0:F3} seconds ({1:F3} processing commands, {2:F3} checksumming).", (end - start).TotalSeconds, totalDuration / 1000, totalChkDuration / 1000);
#pragma warning disable IDE0004 // Cast is necessary, otherwise incorrect value is created
            DicConsole.WriteLine("Avegare speed: {0:F3} MiB/sec.", (((double)blockSize * (double)(blocks + 1)) / 1048576) / (totalDuration / 1000));
#pragma warning restore IDE0004 // Cast is necessary, otherwise incorrect value is created
            DicConsole.WriteLine("Fastest speed burst: {0:F3} MiB/sec.", maxSpeed);
            DicConsole.WriteLine("Slowest speed burst: {0:F3} MiB/sec.", minSpeed);
            DicConsole.WriteLine("{0} sectors could not be read.", unreadableSectors.Count);
            if(unreadableSectors.Count > 0)
            {
                unreadableSectors.Sort();
                foreach(ulong bad in unreadableSectors)
                    DicConsole.WriteLine("Sector {0} could not be read", bad);
            }
            DicConsole.WriteLine();

            if(!aborted)
            {
                DicConsole.WriteLine("Writing metadata sidecar");

                FileStream xmlFs = new FileStream(outputPrefix + ".cicm.xml",
                                       FileMode.Create);

                System.Xml.Serialization.XmlSerializer xmlSer = new System.Xml.Serialization.XmlSerializer(typeof(CICMMetadataType));
                xmlSer.Serialize(xmlFs, sidecar);
                xmlFs.Close();
            }

            Statistics.AddMedia(dskType, true);
        }
    }
}