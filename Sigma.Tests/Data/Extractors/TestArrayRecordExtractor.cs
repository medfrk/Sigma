﻿/* 
MIT License

Copyright (c) 2016-2017 Florian Cäsar, Michael Plainer

For full license see LICENSE in the root directory of this project. 
*/

using NUnit.Framework;
using Sigma.Core.Data.Extractors;
using Sigma.Core.Data.Sources;
using Sigma.Core.Handlers;
using System;
using System.Collections.Generic;
using System.IO;
using Sigma.Core.Handlers.Backends.SigmaDiff.NativeCpu;

namespace Sigma.Tests.Data.Extractors
{
	public class TestArrayRecordExtractor
	{
		private static void CreateCsvTempFile(string name)
		{
			File.Create(Path.GetTempPath() + name).Dispose();

			//no idea how this part could be done better without making a mess
			File.WriteAllBytes(Path.GetTempPath() + name, new byte[] { 0, 0, 5, 3, 4 });
		}

		private static void DeleteTempFile(string name)
		{
			File.Delete(Path.GetTempPath() + name);
		}

		[TestCase]
		public void TestByteRecordExtractorCreate()
		{
			string filename = ".unittestfile" + nameof(TestByteRecordExtractorCreate);

			CreateCsvTempFile(filename);

			FileSource source = new FileSource(filename, Path.GetTempPath());

			Assert.Throws<ArgumentNullException>(() => new ArrayRecordExtractor<byte>(null));
			Assert.Throws<ArgumentException>(() => new ArrayRecordExtractor<byte>(new Dictionary<string, long[][]>() { ["test"] = new long[1][] { new long[] { 1, 2, 3 } } }));

			ArrayRecordExtractor<byte> extractor = new ArrayRecordExtractor<byte>(ArrayRecordExtractor<byte>.ParseExtractorParameters("inputs", new[] { 0L }, new[] { 1L }));

			Assert.AreEqual(new[] { "inputs" }, extractor.SectionNames);

			source.Dispose();

			DeleteTempFile(filename);
		}

		[TestCase]
		public void TestByteRecordExtractorExtract()
		{
			ArrayRecordExtractor<byte> extractor = new ArrayRecordExtractor<byte>(ArrayRecordExtractor<byte>.ParseExtractorParameters("inputs", new[] { 0L }, new[] { 1L }));
			IComputationHandler handler = new CpuFloat32Handler();

			Assert.Throws<InvalidOperationException>(() => extractor.ExtractDirect(10, handler));

			byte[][] rawData = new[] { new byte[] { 0 }, new byte[] { 1 } };

			Assert.AreEqual(new float[] { 0, 1 }, extractor.ExtractDirectFrom(rawData, 2, handler)["inputs"].GetDataAs<float>().GetValuesArrayAs<float>(0L, 2L));
		}
	}
}
