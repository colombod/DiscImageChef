﻿// /***************************************************************************
// The Disc Image Chef
// ----------------------------------------------------------------------------
//
// Filename       : SHA384.cs
// Author(s)      : Natalia Portillo <claunia@claunia.com>
//
// Component      : DiscImageChef unit testing.
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
// Copyright © 2011-2018 Natalia Portillo
// ****************************************************************************/

using System.IO;
using DiscImageChef.Checksums;
using NUnit.Framework;

namespace DiscImageChef.Tests.Checksums
{
    [TestFixture]
    public class SHA384
    {
        static readonly byte[] ExpectedEmpty =
        {
            0x31, 0x64, 0x67, 0x3a, 0x8a, 0xc2, 0x75, 0x76, 0xab, 0x5f, 0xc0, 0x6b, 0x9a, 0xdc, 0x4c, 0xe0, 0xac, 0xa5,
            0xbd, 0x30, 0x25, 0x38, 0x4b, 0x1c, 0xf2, 0x12, 0x8a, 0x87, 0x95, 0xe7, 0x47, 0xc4, 0x31, 0xe8, 0x82, 0x78,
            0x5a, 0x0b, 0xf8, 0xdc, 0x70, 0xb4, 0x29, 0x95, 0xdb, 0x38, 0x85, 0x75
        };
        static readonly byte[] ExpectedRandom =
        {
            0xdb, 0x53, 0x0e, 0x17, 0x9b, 0x81, 0xfe, 0x5f, 0x6d, 0x20, 0x41, 0x04, 0x6e, 0x77, 0xd9, 0x85, 0xf2, 0x85,
            0x8a, 0x66, 0xca, 0xd3, 0x8d, 0x1a, 0xd5, 0xac, 0x67, 0xa9, 0x74, 0xe1, 0xef, 0x3f, 0x4d, 0xdf, 0x94, 0x15,
            0x2e, 0xac, 0x2e, 0xfe, 0x16, 0x95, 0x81, 0x54, 0xdc, 0x59, 0xd4, 0xc3
        };

        [Test]
        public void SHA384EmptyFile()
        {
            Sha384Context ctx = new Sha384Context();
            ctx.Init();
            byte[] result = ctx.File(Path.Combine(Consts.TestFilesRoot, "checksums", "empty"));
            Assert.AreEqual(ExpectedEmpty, result);
        }

        [Test]
        public void SHA384EmptyData()
        {
            byte[] data = new byte[1048576];
            FileStream fs = new FileStream(Path.Combine(Consts.TestFilesRoot, "checksums", "empty"), FileMode.Open,
                                           FileAccess.Read);
            fs.Read(data, 0, 1048576);
            fs.Close();
            fs.Dispose();
            Sha384Context ctx = new Sha384Context();
            ctx.Init();
            ctx.Data(data, out byte[] result);
            Assert.AreEqual(ExpectedEmpty, result);
        }

        [Test]
        public void SHA384EmptyInstance()
        {
            byte[] data = new byte[1048576];
            FileStream fs = new FileStream(Path.Combine(Consts.TestFilesRoot, "checksums", "empty"), FileMode.Open,
                                           FileAccess.Read);
            fs.Read(data, 0, 1048576);
            fs.Close();
            fs.Dispose();
            Sha384Context ctx = new Sha384Context();
            ctx.Init();
            ctx.Update(data);
            byte[] result = ctx.Final();
            Assert.AreEqual(ExpectedEmpty, result);
        }

        [Test]
        public void SHA384RandomFile()
        {
            Sha384Context ctx = new Sha384Context();
            ctx.Init();
            byte[] result = ctx.File(Path.Combine(Consts.TestFilesRoot, "checksums", "random"));
            Assert.AreEqual(ExpectedRandom, result);
        }

        [Test]
        public void SHA384RandomData()
        {
            byte[] data = new byte[1048576];
            FileStream fs = new FileStream(Path.Combine(Consts.TestFilesRoot, "checksums", "random"), FileMode.Open,
                                           FileAccess.Read);
            fs.Read(data, 0, 1048576);
            fs.Close();
            fs.Dispose();
            Sha384Context ctx = new Sha384Context();
            ctx.Init();
            ctx.Data(data, out byte[] result);
            Assert.AreEqual(ExpectedRandom, result);
        }

        [Test]
        public void SHA384RandomInstance()
        {
            byte[] data = new byte[1048576];
            FileStream fs = new FileStream(Path.Combine(Consts.TestFilesRoot, "checksums", "random"), FileMode.Open,
                                           FileAccess.Read);
            fs.Read(data, 0, 1048576);
            fs.Close();
            fs.Dispose();
            Sha384Context ctx = new Sha384Context();
            ctx.Init();
            ctx.Update(data);
            byte[] result = ctx.Final();
            Assert.AreEqual(ExpectedRandom, result);
        }
    }
}