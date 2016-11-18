﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sigma.Core.Handlers;
using Sigma.Core.MathAbstract;

namespace Sigma.Core.Training.Initialisers
{
	/// <summary>
	/// A constant value initialiser, which initialises ndarrays with one constant value.
	/// </summary>
	public class ConstantValueInitialiser : IInitialiser
	{
		/// <summary>
		/// The constant value to initialise with.
		/// </summary>
		public double ConstantValue { get; set; }

		/// <summary>
		/// Create a constant value initialiser for a certain constant value.
		/// </summary>
		/// <param name="constantValue">The constant value.</param>
		public ConstantValueInitialiser(double constantValue)
		{
			this.ConstantValue = constantValue;
		}

		/// <summary>
		/// Create a constant value initialiser for a certain constant value.
		/// </summary>
		/// <param name="constantValue">The constant value.</param>
		/// <returns>A constant value initialiser with the given constant value.</returns>
		public ConstantValueInitialiser Constant(double constantValue)
		{
			return new ConstantValueInitialiser(constantValue);
		}

		public void Initialise(INDArray array, IComputationHandler handler, Random random)
		{
			if (array == null)
			{
				throw new ArgumentNullException("Array cannot be null.");
			}

			if (handler == null)
			{
				throw new ArgumentNullException("Handler cannot be null.");
			}

			if (random == null)
			{
				throw new ArgumentNullException("Random cannot be null.");
			}

			handler.Fill(ConstantValue, array);
		}
	}
}