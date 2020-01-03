﻿// /***************************************************************************
// The Disc Image Chef
// ----------------------------------------------------------------------------
//
// Filename       : QNX6.cs
// Author(s)      : Natalia Portillo <claunia@claunia.com>
//
// Component      : QNX6 filesystem plugin.
//
// --[ Description ] ----------------------------------------------------------
//
//     Identifies the QNX6 filesystem and shows information.
//
// --[ License ] --------------------------------------------------------------
//
//     This library is free software; you can redistribute it and/or modify
//     it under the terms of the GNU Lesser General Public License as
//     published by the Free Software Foundation; either version 2.1 of the
//     License, or (at your option) any later version.
//
//     This library is distributed in the hope that it will be useful, but
//     WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//     Lesser General Public License for more details.
//
//     You should have received a copy of the GNU Lesser General Public
//     License along with this library; if not, see <http://www.gnu.org/licenses/>.
//
// ----------------------------------------------------------------------------
// Copyright © 2011-2020 Natalia Portillo
// ****************************************************************************/

using System;
using System.Runtime.InteropServices;
using System.Text;
using DiscImageChef.CommonTypes;
using DiscImageChef.CommonTypes.Interfaces;
using Schemas;
using Marshal = DiscImageChef.Helpers.Marshal;

namespace DiscImageChef.Filesystems
{
    public class QNX6 : IFilesystem
    {
        const uint QNX6_SUPER_BLOCK_SIZE = 0x1000;
        const uint QNX6_BOOT_BLOCKS_SIZE = 0x2000;
        const uint QNX6_MAGIC            = 0x68191122;

        public FileSystemType XmlFsType { get; private set; }
        public Encoding       Encoding  { get; private set; }
        public string         Name      => "QNX6 Plugin";
        public Guid           Id        => new Guid("3E610EA2-4D08-4D70-8947-830CD4C74FC0");
        public string         Author    => "Natalia Portillo";

        public bool Identify(IMediaImage imagePlugin, Partition partition)
        {
            uint sectors     = QNX6_SUPER_BLOCK_SIZE / imagePlugin.Info.SectorSize;
            uint bootSectors = QNX6_BOOT_BLOCKS_SIZE / imagePlugin.Info.SectorSize;

            if(partition.Start + bootSectors + sectors >= partition.End) return false;

            byte[] audiSector = imagePlugin.ReadSectors(partition.Start,               sectors);
            byte[] sector     = imagePlugin.ReadSectors(partition.Start + bootSectors, sectors);
            if(sector.Length < QNX6_SUPER_BLOCK_SIZE) return false;

            QNX6_AudiSuperBlock audiSb = Marshal.ByteArrayToStructureLittleEndian<QNX6_AudiSuperBlock>(audiSector);

            QNX6_SuperBlock qnxSb = Marshal.ByteArrayToStructureLittleEndian<QNX6_SuperBlock>(sector);

            return qnxSb.magic == QNX6_MAGIC || audiSb.magic == QNX6_MAGIC;
        }

        public void GetInformation(IMediaImage imagePlugin, Partition partition, out string information,
                                   Encoding    encoding)
        {
            Encoding    = encoding ?? Encoding.GetEncoding("iso-8859-15");
            information = "";
            StringBuilder sb          = new StringBuilder();
            uint          sectors     = QNX6_SUPER_BLOCK_SIZE / imagePlugin.Info.SectorSize;
            uint          bootSectors = QNX6_BOOT_BLOCKS_SIZE / imagePlugin.Info.SectorSize;

            byte[] audiSector = imagePlugin.ReadSectors(partition.Start,               sectors);
            byte[] sector     = imagePlugin.ReadSectors(partition.Start + bootSectors, sectors);
            if(sector.Length < QNX6_SUPER_BLOCK_SIZE) return;

            QNX6_AudiSuperBlock audiSb = Marshal.ByteArrayToStructureLittleEndian<QNX6_AudiSuperBlock>(audiSector);

            QNX6_SuperBlock qnxSb = Marshal.ByteArrayToStructureLittleEndian<QNX6_SuperBlock>(sector);

            bool audi = audiSb.magic == QNX6_MAGIC;

            if(audi)
            {
                sb.AppendLine("QNX6 (Audi) filesystem");
                sb.AppendFormat("Checksum: 0x{0:X8}", audiSb.checksum).AppendLine();
                sb.AppendFormat("Serial: 0x{0:X16}", audiSb.checksum).AppendLine();
                sb.AppendFormat("{0} bytes per block", audiSb.blockSize).AppendLine();
                sb.AppendFormat("{0} inodes free of {1}", audiSb.freeInodes, audiSb.numInodes).AppendLine();
                sb.AppendFormat("{0} blocks ({1} bytes) free of {2} ({3} bytes)", audiSb.freeBlocks,
                                audiSb.freeBlocks * audiSb.blockSize, audiSb.numBlocks,
                                audiSb.numBlocks  * audiSb.blockSize).AppendLine();

                XmlFsType = new FileSystemType
                {
                    Type                  = "QNX6 (Audi) filesystem",
                    Clusters              = audiSb.numBlocks,
                    ClusterSize           = audiSb.blockSize,
                    Bootable              = true,
                    Files                 = audiSb.numInodes - audiSb.freeInodes,
                    FilesSpecified        = true,
                    FreeClusters          = audiSb.freeBlocks,
                    FreeClustersSpecified = true,
                    VolumeSerial          = $"{audiSb.serial:X16}"
                };
                //xmlFSType.VolumeName = CurrentEncoding.GetString(audiSb.id);

                information = sb.ToString();
                return;
            }

            sb.AppendLine("QNX6 filesystem");
            sb.AppendFormat("Checksum: 0x{0:X8}", qnxSb.checksum).AppendLine();
            sb.AppendFormat("Serial: 0x{0:X16}", qnxSb.checksum).AppendLine();
            sb.AppendFormat("Created on {0}", DateHandlers.UnixUnsignedToDateTime(qnxSb.ctime)).AppendLine();
            sb.AppendFormat("Last mounted on {0}", DateHandlers.UnixUnsignedToDateTime(qnxSb.atime)).AppendLine();
            sb.AppendFormat("Flags: 0x{0:X8}", qnxSb.flags).AppendLine();
            sb.AppendFormat("Version1: 0x{0:X4}", qnxSb.version1).AppendLine();
            sb.AppendFormat("Version2: 0x{0:X4}", qnxSb.version2).AppendLine();
            //sb.AppendFormat("Volume ID: \"{0}\"", CurrentEncoding.GetString(qnxSb.volumeid)).AppendLine();
            sb.AppendFormat("{0} bytes per block", qnxSb.blockSize).AppendLine();
            sb.AppendFormat("{0} inodes free of {1}", qnxSb.freeInodes, qnxSb.numInodes).AppendLine();
            sb.AppendFormat("{0} blocks ({1} bytes) free of {2} ({3} bytes)", qnxSb.freeBlocks,
                            qnxSb.freeBlocks * qnxSb.blockSize, qnxSb.numBlocks, qnxSb.numBlocks * qnxSb.blockSize)
              .AppendLine();

            XmlFsType = new FileSystemType
            {
                Type                      = "QNX6 filesystem",
                Clusters                  = qnxSb.numBlocks,
                ClusterSize               = qnxSb.blockSize,
                Bootable                  = true,
                Files                     = qnxSb.numInodes - qnxSb.freeInodes,
                FilesSpecified            = true,
                FreeClusters              = qnxSb.freeBlocks,
                FreeClustersSpecified     = true,
                VolumeSerial              = $"{qnxSb.serial:X16}",
                CreationDate              = DateHandlers.UnixUnsignedToDateTime(qnxSb.ctime),
                CreationDateSpecified     = true,
                ModificationDate          = DateHandlers.UnixUnsignedToDateTime(qnxSb.atime),
                ModificationDateSpecified = true
            };
            //xmlFSType.VolumeName = CurrentEncoding.GetString(qnxSb.volumeid);

            information = sb.ToString();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct QNX6_RootNode
        {
            public readonly ulong size;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public readonly uint[] pointers;
            public readonly byte levels;
            public readonly byte mode;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public readonly byte[] spare;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct QNX6_SuperBlock
        {
            public readonly uint   magic;
            public readonly uint   checksum;
            public readonly ulong  serial;
            public readonly uint   ctime;
            public readonly uint   atime;
            public readonly uint   flags;
            public readonly ushort version1;
            public readonly ushort version2;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public readonly byte[] volumeid;
            public readonly uint          blockSize;
            public readonly uint          numInodes;
            public readonly uint          freeInodes;
            public readonly uint          numBlocks;
            public readonly uint          freeBlocks;
            public readonly uint          allocationGroup;
            public readonly QNX6_RootNode inode;
            public readonly QNX6_RootNode bitmap;
            public readonly QNX6_RootNode longfile;
            public readonly QNX6_RootNode unknown;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct QNX6_AudiSuperBlock
        {
            public readonly uint  magic;
            public readonly uint  checksum;
            public readonly ulong serial;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
            public readonly byte[] spare1;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
            public readonly byte[] id;
            public readonly uint          blockSize;
            public readonly uint          numInodes;
            public readonly uint          freeInodes;
            public readonly uint          numBlocks;
            public readonly uint          freeBlocks;
            public readonly uint          spare2;
            public readonly QNX6_RootNode inode;
            public readonly QNX6_RootNode bitmap;
            public readonly QNX6_RootNode longfile;
            public readonly QNX6_RootNode unknown;
        }
    }
}