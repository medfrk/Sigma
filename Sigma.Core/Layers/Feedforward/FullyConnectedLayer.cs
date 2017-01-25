﻿/* 
MIT License

Copyright (c) 2016-2017 Florian Cäsar, Michael Plainer

For full license see LICENSE in the root directory of this project. 
*/

using Sigma.Core.Architecture;
using Sigma.Core.Handlers;
using Sigma.Core.MathAbstract;
using Sigma.Core.Utils;

namespace Sigma.Core.Layers.Feedforward
{
	/// <summary>
	/// A fully connected layer with fully connected nodes to the previous layer.
	/// </summary>
	public class FullyConnectedLayer : BaseLayer
	{
		public FullyConnectedLayer(string name, IRegistry parameters, IComputationHandler handler) : base(name, parameters, handler)
		{
			int size = parameters.Get<int>("size");
			int inputSize = parameters.Get<int>("default_input_size");

			parameters["weights"] = handler.NDArray(inputSize, size);
			parameters["biases"] = handler.NDArray(size);

			TrainableParameters = new[] { "weights" };
		}

		public override void Run(ILayerBuffer buffer, IComputationHandler handler, bool trainingPass)
		{
			INDArray activations = handler.FlattenTimeAndFeatures(buffer.Inputs["default"].Get<INDArray>("activations"));
			INDArray weights = buffer.Parameters.Get<INDArray>("weights");
			//INDArray biases = buffer.Parameters.Get<INDArray>("biases");
			// TODO add add_mv operation with vector-to-matrix broadcasting for ndarrays (e.g. for biases in this case)

			activations = handler.Dot(activations, weights);
			activations = handler.Activation(buffer.Parameters.Get<string>("activation"), activations);

			buffer.Outputs["default"]["activations"] = activations;
		}

		public static LayerConstruct Construct(int size, string activation = "tanh", string name = "#-fullyconnected")
		{
			LayerConstruct construct = new LayerConstruct(name, typeof(FullyConnectedLayer));

			construct.Parameters["size"] = size;
			construct.Parameters["activation"] = activation;

			// input size is required for instantiation but not known at construction time, so update before instantiation
			construct.UpdateBeforeInstantiationEvent +=
				(sender, args) => args.Self.Parameters["default_input_size"] = args.Self.Inputs["default"].Parameters["size"];

			return construct;
		}
	}
}