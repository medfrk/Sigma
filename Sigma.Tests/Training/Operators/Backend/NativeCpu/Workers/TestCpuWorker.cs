﻿using System;
using System.Threading;
using NUnit.Framework;
using Sigma.Core.Architecture;
using Sigma.Core.Training.Operators;
using Sigma.Core.Training.Operators.Backends.NativeCpu;
using Sigma.Core.Training.Operators.Backends.NativeCpu.Workers;

namespace Sigma.Tests.Training.Operators.Backend.NativeCpu.Workers
{
	public class TestCpuWorker
	{
		private static CpuWorker CreateCpuWorker()
		{
			return new CpuWorker(CreateOperator());
		}

		private static IOperator CreateOperator()
		{
			IOperator @operator =new CpuMultithreadedOperator(10);

			@operator.Network = new Network();

			return @operator;
		}

		[TestCase]
		public void TestCpuWorkerCreate()
		{
			IOperator oOperator = new CpuMultithreadedOperator(10);
			CpuWorker worker = new CpuWorker(oOperator, oOperator.Handler, ThreadPriority.Normal);

			Assert.AreSame(oOperator, worker.Operator);
			Assert.AreEqual(worker.ThreadPriority, ThreadPriority.Normal);
			Assert.AreSame(worker.Handler, oOperator.Handler);

			worker = new CpuWorker(oOperator);

			Assert.AreSame(worker.Handler, oOperator.Handler);

			Assert.AreEqual(worker.State, ExecutionState.None);
		}

		[TestCase]
		public void TestCpuWorkerStart()
		{
			CpuWorker worker = CreateCpuWorker();

			worker.Start();
			Assert.AreEqual(worker.State, ExecutionState.Running);

			worker.Start();
			Assert.AreEqual(worker.State, ExecutionState.Running);

			worker.SignalStop();
			Assert.AreNotEqual(worker.State, ExecutionState.Running);

			worker.Start();
			Assert.AreEqual(worker.State, ExecutionState.Running);

			worker.SignalPause();
			Assert.AreNotEqual(worker.State, ExecutionState.Running);

			Assert.Throws<InvalidOperationException>(() => worker.Start());
			Assert.AreNotEqual(worker.State, ExecutionState.Running);

			worker.SignalStop();
		}

		[TestCase]
		public void TestCpuWorkerSignalPause()
		{
			CpuWorker worker = CreateCpuWorker();
			worker.Start();

			worker.SignalPause();
			Assert.AreEqual(worker.State, ExecutionState.Paused);

			worker.SignalPause();
			Assert.AreEqual(worker.State, ExecutionState.Paused);

			worker.SignalStop();
			Assert.AreNotEqual(worker.State, ExecutionState.Paused);

			Assert.Throws<InvalidOperationException>(() => worker.SignalPause());
			Assert.AreNotEqual(worker.State, ExecutionState.Paused);
		}


		[TestCase]
		public void TestCpuWorkerSignalResume()
		{
			CpuWorker worker = CreateCpuWorker();
			worker.Start();

			worker.SignalPause();
			worker.SignalResume();
			Assert.AreEqual(worker.State, ExecutionState.Running);

			worker.SignalResume();
			Assert.AreEqual(worker.State, ExecutionState.Running);

			worker.SignalStop();
			Assert.Throws<InvalidOperationException>(() => worker.SignalResume());

			worker.SignalStop();
		}

		[TestCase]
		public void TestCpuWorkerSignalStop()
		{
			CpuWorker worker = CreateCpuWorker();
			worker.Start();

			worker.SignalStop();
			Assert.AreEqual(worker.State, ExecutionState.Stopped);

			worker.SignalStop();
			Assert.AreEqual(worker.State, ExecutionState.Stopped);

			worker.Start();
			Assert.AreNotEqual(worker.State, ExecutionState.Stopped);

			worker.SignalPause();
			worker.SignalStop();
			Assert.AreEqual(worker.State, ExecutionState.Stopped);

			worker.Start();
			worker.SignalPause();
			worker.SignalResume();
			worker.SignalStop();
			Assert.AreEqual(worker.State, ExecutionState.Stopped);
		}
	}
}