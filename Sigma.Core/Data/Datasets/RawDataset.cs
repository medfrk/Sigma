﻿/*
MIT License

Copyright(c) 2016-2017 Florian Cäsar, Michael Plainer

For full license see LICENSE in the root directory of this project.
*/


using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sigma.Core.Handlers;
using Sigma.Core.Handlers.Backends.SigmaDiff.NativeCpu;
using Sigma.Core.MathAbstract;
using Sigma.Core.Utils;

namespace Sigma.Core.Data.Datasets
{
    /// <summary>
    /// A raw in-system-memory dataset which can be manually 
    /// </summary>
    public class RawDataset : IDataset
    {
        private readonly IComputationHandler _internalHandler;

        /// <summary>
        /// The name and identifier of this dataset.
        /// Dataset names should be globally unique and easily identifiable. 
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Indicate if this dataset is an online dataset (meaning new data might be added during runtime).
        /// By default, this is assumed to be false, indicating a static dataset.
        /// Note: Data iterators and may perform certain optimisations for static datasets, so set this to false if possible.
        /// </summary>
        public bool Online { get; set; }

        /// <summary>
        /// The preferred per block size in records.
        /// Note: Not every block must obey this request (e.g. the last black might very well be a different size).
        /// </summary>
        public int TargetBlockSizeRecords { get; }

        /// <summary>
        /// The maximum number of concurrently active blocks. 
        /// </summary>
        public int MaxConcurrentActiveBlocks { get; }

        /// <summary>
        /// The maximum total concurrently active block size in bytes.
        /// </summary>
        public long MaxTotalActiveBlockSizeBytes { get; }

        /// <summary>
        /// The total size of all currently active record blocks in system memory in bytes.
        /// </summary>
        public long TotalActiveBlockSizeBytes { get; }

        /// <summary>
        /// The maxmimum number of blocks to keep in the cache (inactive blocks are written to a cache, typically on disk, to be reloaded later).
        /// </summary>
        public int MaxBlocksInCache { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }

        /// <summary>
        /// The maxmimum number of bytes to keep in the cache (inactive blocks are written to a cache, typically on disk, to be reloaded later).
        /// </summary>
        public long MaxBytesInCache { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }

        /// <summary>
        /// The names for all sections present in this dataset (e.g. "inputs", "targets").
        /// </summary>
        public string[] SectionNames { get; }

        /// <summary>
        /// A set of currently active and loaded record block indices. 
        /// </summary>
        public IReadOnlyCollection<int> ActiveBlockIndices { get; }

        /// <summary>
        /// The number of currently active and loaded record blocks, with different block formats counting as different blocks. 
        /// </summary>
        public int ActiveIndividualBlockCount { get; }

        /// <summary>
        /// The number of currently active and loaded record blocks, with different block formats of the same region counting as one active block index.
        /// </summary>
        public int ActiveBlockRegionCount { get; }

        /// <summary>
        /// The current working data that can be edited and is "flushed" to the public raw data with the next <see cref="FetchBlock"/> call.
        /// </summary>
        private readonly IDictionary<string, INDArray> _internalWorkingData;

        /// <summary>
        /// The raw data of this dataset that is returned via the <see cref="FetchBlock"/> functions.
        /// </summary>
        private readonly IDictionary<IComputationHandler, IDictionary<string, INDArray>> _rawData;


        /// <summary>
        /// Create a raw dataset with a certain name and an internal cpu handler with 32-bit float precision.
        /// </summary>
        /// <param name="name">The globally unique name of this dataset.</param>
        /// <param name="internalHandler">The internal handler to use for data management.</param>
        public RawDataset(string name) : this(name, new CpuFloat32Handler())
        {
        }

        /// <summary>
        /// Create a raw dataset with a certain name and computation handler.
        /// </summary>
        /// <param name="name">The globally unique name of this dataset.</param>
        /// <param name="internalHandler">The internal handler to use for data management.</param>
        public RawDataset(string name, IComputationHandler internalHandler)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (internalHandler == null) throw new ArgumentNullException(nameof(internalHandler));

            Name = name;
            _internalHandler = internalHandler;

            _internalWorkingData = new Dictionary<string, INDArray>();
            _rawData = new ConcurrentDictionary<IComputationHandler, IDictionary<string, INDArray>>();
        }

        public void AddRecord<T>(string blockName, params T[] record)
        {
            AddShapedRecords<T>(blockName, new long[] { record.Length }, new[] { record });
        }

        public void AddRecord<T>(string blockName, long[] featureShape, params T[][] records)
        {
            AddShapedRecords<T>(blockName, featureShape, records);
        }

        public void AddRecords<T>(string blockName, params T[][] records)
        {
            AddShapedRecords<T>(blockName, new long[] { records[0].Length }, records);
        }

        public void AddShapedRecords<T>(string blockName, long[] featureShape, params T[][] records)
        {
            if (records.Length == 0)
            {
                return;
            }

            lock (_internalWorkingData)
            {
                long featureLength = ArrayUtils.Product(featureShape);
                long[] insertedShape = ArrayUtils.Concatenate(new long[] { records.Length, 1 }, featureShape); // BatchTimeFeatures shape order, time dimension is not supported at the moment
                long[] newShape = (long[])insertedShape.Clone();
                bool previousBlockExists = _internalWorkingData.ContainsKey(blockName);

                if (previousBlockExists)
                {
                    insertedShape[0] += _internalWorkingData[blockName].Shape[0]; // append new record to end
                }

                INDArray newBlock = _internalHandler.NDArray(newShape);

                long[] destinationBegin = new long[newBlock.Rank];

                if (previousBlockExists)
                {
                    INDArray oldBlock = _internalWorkingData[blockName];

                    long[] previousSourceBegin = new long[oldBlock.Shape.Rank];
                    long[] previousSourceEnd = oldBlock.Shape.Select(i => i - 1).ToArray();

                    _internalHandler.Fill(oldBlock, newBlock, previousSourceBegin, previousSourceEnd, previousSourceBegin, previousSourceEnd);

                    destinationBegin[0] = oldBlock.Shape[0];
                }

                long[] destinationEnd = newShape.Select(i => i - 1).ToArray();
                destinationEnd[0] = destinationBegin[0];

                for (int i = 0; i < records.Length; i++)
                {
                    _internalHandler.Fill(records[i], newBlock, destinationBegin, destinationEnd);

                    destinationBegin[0]++;
                    destinationEnd[0]++;
                } // values aren't set correctly, shifted around and just wrong - TODO fix

                if (previousBlockExists)
                {
                    _internalWorkingData[blockName] = newBlock;
                }
                else
                {
                    _internalWorkingData.Add(blockName, newBlock);
                }
            }
        }

        /// <inheritdoc />
        public IDictionary<string, INDArray> FetchBlock(int blockIndex, IComputationHandler handler, bool shouldWaitUntilAvailable = true)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public Task<IDictionary<string, INDArray>> FetchBlockAsync(int blockIndex, IComputationHandler handler, bool shouldWaitUntilAvailable = true)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public void FreeBlock(int blockIndex, IComputationHandler handler)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public bool IsBlockActive(int blockIndex)
        {
            return blockIndex == 0;
        }

        /// <inheritdoc />
        public bool IsBlockActive(int blockIndex, IComputationHandler handler)
        {
            return blockIndex == 0 && _rawData.ContainsKey(handler);
        }

        /// <inheritdoc />
        public long GetBlockSizeBytes(int blockIndex, IComputationHandler handler)
        {
            if (!IsBlockActive(blockIndex, handler))
            {
                return -1L;
            }

            // TODO
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public bool CanFetchBlocksAfter(int blockIndex)
        {
            return blockIndex == -1;
        }

        /// <inheritdoc />
        public bool TrySetBlockSize(int blockSizeRecords)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public IDataset[] SplitBlockwise(params int[] parts)
        {
            return ExtractedDataset.SplitBlockwise(this, parts);
        }

        /// <inheritdoc />
        public IDataset[] SplitRecordwise(params double[] percentages)
        {
            return ExtractedDataset.SplitRecordwise(this, percentages);
        }

        /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
        public void Dispose()
        {

        }
    }
}
