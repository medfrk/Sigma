﻿/* 
MIT License

Copyright (c) 2016-2017 Florian Cäsar, Michael Plainer

For full license see LICENSE in the root directory of this project. 
*/

using log4net;
using log4net.Appender;
using log4net.Config;
using log4net.Core;
using log4net.Filter;
using log4net.Layout;
using log4net.Repository.Hierarchy;
using Sigma.Core.Monitors;
using Sigma.Core.Monitors.Synchronisation;
using Sigma.Core.Parameterisation;
using Sigma.Core.Persistence;
using Sigma.Core.Training;
using Sigma.Core.Training.Hooks;
using Sigma.Core.Training.Operators;
using Sigma.Core.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Sigma.Core
{
	/// <summary>
	/// A sigma environment, where all the magic happens.
	/// </summary>
	[Serializable]
	public class SigmaEnvironment : ISerialisationNotifier
	{
		/// <summary>
		/// If the <see cref="IOperator"/>s should automatically start when calling <see cref="Run"/>.
		/// </summary>
		public bool StartOperatorsOnRun { get; set; } = true;

		/// <summary>
		/// If the <see cref="SigmaEnvironment"/> is currently running. 
		/// </summary>
		public bool Running { get; private set; }

		private readonly ISet<IMonitor> _monitors;
		private readonly IDictionary<string, ITrainer> _trainersByName;
		public readonly IDictionary<ITrainer, IOperator> RunningOperatorsByTrainer;
		private readonly ConcurrentQueue<KeyValuePair<IHook, IOperator>> _globalHooksToAttach;
		private readonly ConcurrentQueue<KeyValuePair<IHook, IOperator>> _localHooksToAttach;
		private bool _requestedStop;

		[NonSerialized]
		private ManualResetEvent _processQueueEvent;

		[NonSerialized]
		private readonly ILog _logger = LogManager.GetLogger(typeof(SigmaEnvironment));

		/// <summary>
		/// The unique name of this environment. 
		/// </summary>
		public string Name { get; }
		/// <summary>
		/// The root registry of this environment where all exposed parameters are stored hierarchically.
		/// </summary>
		public IRegistry Registry { get; }

		/// <summary>
		/// The registry resolver corresponding to the registry used in this environment. 
		/// For easier notation and faster access you can retrieve and using regex-style registry names and dot notation.
		/// </summary>
		public IRegistryResolver RegistryResolver { get; }

		/// <summary>
		/// The random number generator to use for randomised operations for reproducibility. 
		/// </summary>
		public Random Random { get; private set; }

		/// <summary>
		/// The parameterisation manager which defines which values to parameterise as what (e.g. range, enumeration, check box).
		/// </summary>
		public IParameterisationManager ParameterisationManager { get; }

		//TODO: put into trainer
		/// <summary>
		/// The handler that is responsible for syncing the monitor with operators / workers. 
		/// </summary>
		public ISynchronisationHandler SynchronisationHandler { get; }

		public int RandomSeed { get; private set; }

		private SigmaEnvironment(string name)
		{
			Name = name;
			Registry = new Registry();
			RegistryResolver = new RegistryResolver(Registry);
			ParameterisationManager = new ParameterisationManager();
			SynchronisationHandler = new SynchronisationHandler(this);

			SetRandomSeed((int)(DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond));

			_monitors = new HashSet<IMonitor>();
			_globalHooksToAttach = new ConcurrentQueue<KeyValuePair<IHook, IOperator>>();
			_localHooksToAttach = new ConcurrentQueue<KeyValuePair<IHook, IOperator>>();
			RunningOperatorsByTrainer = new ConcurrentDictionary<ITrainer, IOperator>();
			_trainersByName = new ConcurrentDictionary<string, ITrainer>();
			_processQueueEvent = new ManualResetEvent(true);
		}

		/// <summary>
		/// Called before this object is serialised.
		/// </summary>
		public void OnSerialising()
		{
		}

		/// <summary>
		/// Called after this object was serialised.
		/// </summary>
		public void OnSerialised()
		{
		}

		/// <summary>
		/// Called after this object was de-serialised. 
		/// </summary>
		public void OnDeserialised()
		{
			_processQueueEvent = new ManualResetEvent(true);
		}

		/// <summary>
		/// Set the random seed used for all randomised computations for reproducibility.
		/// </summary>
		/// <param name="seed"></param>
		public void SetRandomSeed(int seed)
		{
			_logger.Info($"Using random initial seed {seed} for sigma environment \"{Name}\".");

			RandomSeed = seed;
			Random = new Random(seed);
		}

		/// <summary>
		/// Add a monitor to this environment.
		/// </summary>
		/// <typeparam name="TMonitor">The type of the monitor to be added.</typeparam>
		/// <param name="monitor">The monitor to add.</param>
		/// <returns>The monitor given (for convenience).</returns>
		public TMonitor AddMonitor<TMonitor>(TMonitor monitor) where TMonitor : IMonitor
		{
			monitor.Sigma = this;
			monitor.Registry = new Registry(Registry);

			monitor.Initialise();
			_monitors.Add(monitor);

			_logger.Debug($"Added monitor {monitor} to sigma environment \"{Name}\".");

			return monitor;
		}

		public bool RemoveMonitor<TMonitor>(TMonitor monitor) where TMonitor : IMonitor
		{
			bool ret = _monitors.Remove(monitor);

			if (ret)
			{
				_logger.Debug($"Removed monitor {monitor} from sigma environment \"{Name}\".");
			}
			else
			{
				_logger.Info($"Could not remove monitor {monitor} from sigma environment \"{Name}\" - probably it was not added.");
			}
			return ret;
		}

		/// <summary>
		/// Prepare this environment for execution. Start all monitors.
		/// </summary>
		public void Prepare()
		{
			foreach (IMonitor monitor in _monitors)
			{
				monitor.Start();
			}
		}

		/// <summary>
		/// Create a trainer with a certain unique name and add it to this environment.
		/// </summary>
		/// <param name="name">The trainer name.</param>
		/// <returns>A new trainer with the given name.</returns>
		public ITrainer CreateTrainer(string name)
		{
			return AddTrainer(new Trainer(name));
		}

		/// <summary>
		/// Create a trainer with a certain unique name without adding it to this sigma environment.
		/// </summary>
		/// <param name="name">The trainer name.</param>
		/// <returns>A new trainer with the given name.</returns>
		public ITrainer CreateGhostTrainer(string name)
		{
			return new Trainer(name);
		}

		/// <summary>
		/// Add a trainer to this environment.
		/// </summary>
		/// <typeparam name="TTrainer">The type of the trainer to add.</typeparam>
		/// <param name="trainer">The trainer to add.</param>
		/// <returns>The given trainer (for convenience).</returns>
		public TTrainer AddTrainer<TTrainer>(TTrainer trainer) where TTrainer : ITrainer
		{
			if (_trainersByName.ContainsKey(trainer.Name))
			{
				throw new InvalidOperationException($"Trainer with name \"{trainer.Name}\" is already registered in this environment (\"{Name}\").");
			}

			_trainersByName.Add(trainer.Name, trainer);
			trainer.Sigma = this;

			_logger.Debug($"Added trainer {trainer} to sigma environment \"{Name}\".");

			return trainer;
		}

		/// <summary>
		/// Remove a trainer (and its associated operator) from this environment.
		/// Note: Warning, this is probably not what you want. Trainer removal may cause inconsistent behaviour during execution.
		///       If the operator is currently running and cannot be disassociated this method will throw an exception.
		/// </summary>
		/// <param name="trainer">The trainer to remove.</param>
		public void RemoveTrainer(ITrainer trainer)
		{
			if (!_trainersByName.Values.Contains(trainer))
			{
				throw new InvalidOperationException($"Cannot remove trainer {trainer} from sigma environment \"{Name}\" as it does not exist in this environment.");
			}

			if (RunningOperatorsByTrainer.ContainsKey(trainer))
			{
				if (RunningOperatorsByTrainer[trainer].State == ExecutionState.Running)
				{
					throw new InvalidOperationException($"Cannot remove trainer {trainer} from sigma environment \"{Name}\" as its associated operator {RunningOperatorsByTrainer[trainer]} is in execution state {nameof(ExecutionState.Running)}.");
				}

				IOperator @operator = RunningOperatorsByTrainer[trainer];

				RunningOperatorsByTrainer[trainer].Sigma = null;
				RunningOperatorsByTrainer.Remove(trainer);

				_logger.Debug($"Removed operator {@operator} from sigma environment \"{Name}\" in association with trainer {trainer}.");
			}

			if (!_trainersByName.Remove(trainer.Name))
			{
				_logger.Warn($"Inconsistent trainer state: Trainer was added to environment \"{Name}\" as \"{trainer.Name}\" but its name now is \"{Name}\". Names should be constant, will attempt continued execution.");

				var existingPair = _trainersByName.First(pair => pair.Value == trainer);
				_trainersByName.Remove(existingPair.Key);
			}

			_logger.Debug($"Removed trainer {trainer} from sigma environment \"{Name}\".");
		}



		/// <summary>
		/// Run this environment. Execute all registered options until stop is requested.
		/// Note: This method is blocking and executes in the calling thread. 
		/// </summary>
		public void Run()
		{
			StartSigmaEnvironment();

			UpdateSigmaEnvironment();
		}

		/// <summary>
		/// Update the sigma environment and keep everything up and running.
		/// </summary>
		private void UpdateSigmaEnvironment()
		{
			bool shouldRun = true;
			while (shouldRun)
			{
				_logger.Debug("Waiting for processing queue event to signal state change....");

				_processQueueEvent.WaitOne();
				_processQueueEvent.Reset();

				_logger.Debug("Received state change signal by processing queue event, processing...");

				ProcessHooksToAttach();

				if (_requestedStop)
				{
					_logger.Info($"Stopping sigma environment \"{Name}\" as request stop flag was set...");

					shouldRun = false;
				}

				_logger.Debug("Done processing for received state change signal.");
			}

			StopRunningOperators();

			Running = false;

			_logger.Info($"Stopped sigma environment \"{Name}\".");
		}

		/// <summary>
		/// This method starts the Sigma Environment
		/// </summary>
		private void StartSigmaEnvironment()
		{
			_logger.Info($"Starting sigma environment \"{Name}\"...");


			Running = true;

			InitialiseTrainers();
			FetchRunningOperators();

			if (StartOperatorsOnRun)
			{
				StartRunningOperators();
			}

			_logger.Info($"Started sigma environment \"{Name}\".");
		}

		/// <summary>
		/// Run this environment asynchronously. Execute all registered options until stop is requested.
		/// </summary>
		public async Task RunAsync()
		{
			StartSigmaEnvironment();
			await Task.Run(() => UpdateSigmaEnvironment());
		}

		/// <summary>
		/// Prepare and run this sigma environment.
		/// Note: This should only be used with smaller projects, as an early <see cref="Prepare"/> call gives monitors more time to setup and more instant user feedback.
		/// </summary>
		public void PrepareAndRun()
		{
			Prepare();
			Run();
		}

		private void InitialiseTrainers()
		{
			foreach (ITrainer trainer in _trainersByName.Values)
			{
				trainer.Initialise(trainer.Operator.Handler);
			}
		}

		protected void FetchRunningOperators()
		{
			if (_trainersByName.Count == 0)
			{
				_logger.Info($"No trainers attached to this environment ({Name}).");
			}

			_logger.Debug($"Fetching operators from {_trainersByName.Count} trainers in environment \"{Name}\"...");

			foreach (ITrainer trainer in _trainersByName.Values)
			{
				RunningOperatorsByTrainer.Add(trainer, trainer.Operator);

				trainer.Operator.Sigma = this;
			}
		}

		private void StartRunningOperators()
		{
			_logger.Debug($"Starting operators from {_trainersByName.Count} trainers in environment \"{Name}\"...");

			foreach (IOperator op in RunningOperatorsByTrainer.Values)
			{
				op.Start();
			}
		}

		private void StopRunningOperators()
		{
			_logger.Debug($"Stopping operators from {_trainersByName.Count} trainers in environment \"{Name}\"...");

			foreach (IOperator op in RunningOperatorsByTrainer.Values)
			{
				op.SignalStop();
			}
		}

		/// <summary>
		/// Signal this environment to stop execution as soon as possible (if running).
		/// </summary>
		public void SignalStop()
		{
			if (!Running)
			{
				return;
			}

			_logger.Debug($"Stop signal received in environment \"{Name}\".");

			_requestedStop = true;
			_processQueueEvent.Set();
		}

		private void ProcessHooksToAttach()
		{
			while (!_globalHooksToAttach.IsEmpty)
			{
				KeyValuePair<IHook, IOperator> hookPair;

				if (_globalHooksToAttach.TryDequeue(out hookPair))
				{
					hookPair.Value.AttachGlobalHook(hookPair.Key);
				}
			}

			while (!_localHooksToAttach.IsEmpty)
			{
				KeyValuePair<IHook, IOperator> hookPair;

				if (_localHooksToAttach.TryDequeue(out hookPair))
				{
					hookPair.Value.AttachGlobalHook(hookPair.Key);
				}
			}
		}

		/// <summary>
		/// Request for a hook to be attached to a certain trainer's operator (identified by its name).
		/// </summary>
		/// <param name="hook">The hook to attach.</param>
		/// <param name="trainerName">The trainer name whose trainer's operator the hook should be attached to.</param>
		public void RequestAttachGlobalHook(IHook hook, string trainerName)
		{
			if (!_trainersByName.ContainsKey((trainerName)))
			{
				throw new ArgumentException($"Trainer with name {trainerName} is not registered in this environment ({Name}).");
			}

			RequestAttachGlobalHook(hook, _trainersByName[trainerName]);
		}

		/// <summary>
		/// Request for a hook to be attached to a certain trainer's operator.
		/// </summary>
		/// <param name="hook">The hook to attach.</param>
		/// <param name="trainer">The trainer whose operator the hook should be attached to.</param>
		public void RequestAttachGlobalHook(IHook hook, ITrainer trainer)
		{
			if (trainer == null) throw new ArgumentNullException(nameof(trainer));

			RequestAttachGlobalHook(hook, trainer.Operator);
		}

		/// <summary>
		/// Request for a hook to be attached to a certain operator.
		/// </summary>
		/// <param name="hook">The hook to attach.</param>
		/// <param name="operatorToAttachTo">The operator to attach to.</param>
		public void RequestAttachGlobalHook(IHook hook, IOperator operatorToAttachTo)
		{
			if (hook == null) throw new ArgumentNullException(nameof(hook));
			if (operatorToAttachTo == null) throw new ArgumentNullException(nameof(operatorToAttachTo));

			_globalHooksToAttach.Enqueue(new KeyValuePair<IHook, IOperator>(hook, operatorToAttachTo));
			_processQueueEvent.Set();
		}

		/// <summary>
		/// Request for a hook to be attached to a certain trainer's operator (identified by its name).
		/// </summary>
		/// <param name="hook">The hook to attach.</param>
		/// <param name="trainerName">The trainer name whose trainer's operator the hook should be attached to.</param>
		public void RequestAttachLocalHook(IHook hook, string trainerName)
		{
			if (!_trainersByName.ContainsKey((trainerName)))
			{
				throw new ArgumentException($"Trainer with name {trainerName} is not registered in this environment ({Name}).");
			}

			RequestAttachLocalHook(hook, _trainersByName[trainerName]);
		}

		/// <summary>
		/// Request for a hook to be attached to a certain trainer's operator.
		/// </summary>
		/// <param name="hook">The hook to attach.</param>
		/// <param name="trainer">The trainer whose operator the hook should be attached to.</param>
		public void RequestAttachLocalHook(IHook hook, ITrainer trainer)
		{
			if (trainer == null) throw new ArgumentNullException(nameof(trainer));

			RequestAttachLocalHook(hook, trainer.Operator);
		}

		/// <summary>
		/// Request for a hook to be attached to a certain operator.
		/// </summary>
		/// <param name="hook">The hook to attach.</param>
		/// <param name="operatorToAttachTo">The operator to attach to.</param>
		public void RequestAttachLocalHook(IHook hook, IOperator operatorToAttachTo)
		{
			if (hook == null) throw new ArgumentNullException(nameof(hook));
			if (operatorToAttachTo == null) throw new ArgumentNullException(nameof(operatorToAttachTo));

			_localHooksToAttach.Enqueue(new KeyValuePair<IHook, IOperator>(hook, operatorToAttachTo));
			_processQueueEvent.Set();
		}

		/// <summary>
		/// Resolve all matching identifiers in this registry. For the detailed supported syntax <see cref="IRegistryResolver"/>.
		/// </summary>
		/// <typeparam name="T">The most specific common type of the variables to retrieve.</typeparam>
		/// <param name="matchIdentifier">The full match identifier.</param>
		/// <param name="fullMatchedIdentifierArray">The fully matched identifiers corresponding to the given match identifier.</param>
		/// <param name="values">An array of values found at the matching identifiers, filled with the values found at all matching identifiers (for reuse and optimisation if request is issued repeatedly).</param>
		/// <returns>An array of values found at the matching identifiers. The parameter values is used if it is large enough and not null.</returns>
		public T[] ResolveGet<T>(string matchIdentifier, out string[] fullMatchedIdentifierArray, T[] values = null)
		{
			return RegistryResolver.ResolveGet(matchIdentifier, out fullMatchedIdentifierArray, values);
		}

		/// <summary>
		/// Resolve all matching identifiers in this registry. For the detailed supported syntax <see cref="IRegistryResolver"/>.
		/// </summary>
		/// <typeparam name="T">The most specific common type of the variables to retrieve.</typeparam>
		/// <param name="matchIdentifier">The full match identifier.</param>
		/// <param name="values">An array of values found at the matching identifiers, filled with the values found at all matching identifiers (for reuse and optimisation if request is issued repeatedly).</param>
		/// <returns>An array of values found at the matching identifiers. The parameter values is used if it is large enough and not null.</returns>
		public T[] ResolveGet<T>(string matchIdentifier, T[] values = null)
		{
			return RegistryResolver.ResolveGet(matchIdentifier, values);
		}

		/// <summary>
		/// Set a single given value of a certain type to all matching identifiers. For the detailed supported syntax <see cref="IRegistryResolver"/>.
		/// Note: The individual registries might throw an exception if a type-protected value is set to the wrong type.
		/// </summary>
		/// <typeparam name="T">The type of the value.</typeparam>
		/// <param name="matchIdentifier">The full match identifier. </param>
		/// <param name="value">The value to set.</param>
		/// <param name="addIdentifierIfNotExists">Indicate if the last (local) identifier should be added if it doesn't exist yet.</param>
		/// <param name="associatedType">Optionally set the associated type (<see cref="IRegistry"/>). If no associated type is set, the one of the registry will be used (if set). </param>
		/// <returns>A list of fully qualified matches to the match identifier.</returns>
		public string[] ResolveSet<T>(string matchIdentifier, T value, bool addIdentifierIfNotExists = false, Type associatedType = null)
		{
			return RegistryResolver.ResolveSet(matchIdentifier, value, addIdentifierIfNotExists, associatedType);
		}

		/// <summary>Returns a string that represents the current environement.</summary>
		/// <returns>A string that represents the current enviornemnt.</returns>
		public override string ToString()
		{
			return Name;
		}

		// static part of SigmaEnvironment
		#region static

		/// <summary>
		/// The task manager for this environment.
		/// </summary>
		public static ITaskManager TaskManager
		{
			get; internal set;
		}

		internal static readonly CultureInfo DefaultCultureInfo = new CultureInfo("en-GB");

		internal static readonly IRegistry ActiveSigmaEnvironments;
		private static readonly ILog ClazzLogger = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

		static SigmaEnvironment()
		{
			if (!ProcessUtils.Is64BitProcess())
			{
				throw new NotSupportedException("Sigma can only run in 64bit mode, please change your launch configuration to match this mode. See http://sigma.rocks/installation for more details.");
			}


			// logging not initialised
			SetDefaultCulture(DefaultCultureInfo);

			ActiveSigmaEnvironments = new Registry();
			TaskManager = new TaskManager();

			Globals = new Registry();
			RegisterGlobals();
		}

		/// <summary>
		/// This method sets the default culture. 
		/// </summary>
		/// <param name="culture">The culture that will be the new default. </param>
		// This method also exists in BaseLocaleTest.
		private static void SetDefaultCulture(CultureInfo culture)
		{
			Thread.CurrentThread.CurrentCulture = culture;
			CultureInfo.DefaultThreadCurrentCulture = culture;
		}


		/// <summary>
		/// A global variable pool for globally relevant constants (e.g. workspace path).
		/// </summary>
		public static IRegistry Globals { get; }

		/// <summary>
		/// Register all global parameters with an initial value and required associated type. 
		/// </summary>
		private static void RegisterGlobals()
		{
			SetGlobalWorkspacePath("workspace/");
			Globals.Set("web_proxy", WebRequest.DefaultWebProxy, typeof(IWebProxy));
		}

		/// <summary>
		/// Set the global workspace path and re-route its subfolders (e.g. cache, datasets).
		/// If you want to be more specific - e.g. only re-route cache folders - modify the <see cref="Globals"/> registry directly.
		/// </summary>
		/// <param name="path"></param>
		public static void SetGlobalWorkspacePath(string path)
		{
			Globals.Set("workspace_path", "workspace/", typeof(string));
			Globals.Set("cache_path", Globals.Get<string>("workspace_path") + "cache/", typeof(string));
			Globals.Set("datasets_path", Globals.Get<string>("workspace_path") + "datasets/", typeof(string));
			Globals.Set("storage_path", Globals.Get<string>("workspace_path") + "storage/", typeof(string));
		}

		/// <summary>
		/// Loads the log4net configuration either from the corresponding app.config file (see log4net for more details) or by
		/// statically generating a default logger.
		/// </summary>
		/// <param name="xml">If <c>true</c>, the app.config file will be loaded. Otherwise, a default configuration.</param>
		/// <param name="printDebug">If <c>true</c>, debug statements will be printed to console (otherwise only <c>Level.Info</c> and up).</param>
		public static void EnableLogging(bool xml = false, bool printDebug = false)
		{
			if (xml)
			{
				XmlConfigurator.Configure();
			}
			else
			{
				// see https://stackoverflow.com/questions/37213848/best-way-to-access-to-log4net-wrapper-app-config
				Hierarchy hierarchy = (Hierarchy)LogManager.GetRepository();

				PatternLayout patternLayout = new PatternLayout
				{
					ConversionPattern = "%date %level [%thread] %logger{1} - %message%newline"
				};
				patternLayout.ActivateOptions();

				// Create a colored console appender with color mappings and level range [Info, Fatal]
				ColoredConsoleAppender console = new ColoredConsoleAppender
				{
					Threshold = Level.All,
					Layout = patternLayout
				};
				LevelRangeFilter consoleRangeFilter = new LevelRangeFilter
				{
					LevelMin = printDebug ? Level.Debug : Level.Info,
					LevelMax = Level.Fatal
				};
				console.AddFilter(consoleRangeFilter);
				console.AddMapping(new ColoredConsoleAppender.LevelColors
				{
					Level = Level.Debug,
					ForeColor = ColoredConsoleAppender.Colors.White
				});
				console.AddMapping(new ColoredConsoleAppender.LevelColors
				{
					Level = Level.Info,
					ForeColor = ColoredConsoleAppender.Colors.Green
				});
				console.AddMapping(new ColoredConsoleAppender.LevelColors
				{
					Level = Level.Warn,
					ForeColor = ColoredConsoleAppender.Colors.Yellow | ColoredConsoleAppender.Colors.HighIntensity
				});
				console.AddMapping(new ColoredConsoleAppender.LevelColors
				{
					Level = Level.Error,
					ForeColor = ColoredConsoleAppender.Colors.Red | ColoredConsoleAppender.Colors.HighIntensity
				});
				console.AddMapping(new ColoredConsoleAppender.LevelColors
				{
					Level = Level.Fatal,
					ForeColor = ColoredConsoleAppender.Colors.White | ColoredConsoleAppender.Colors.HighIntensity,
					BackColor = ColoredConsoleAppender.Colors.Red | ColoredConsoleAppender.Colors.HighIntensity
				});
				console.ActivateOptions();

				hierarchy.Root.AddAppender(console);

				// Create also an appender that writes the log to sigma.log
				RollingFileAppender roller = new RollingFileAppender
				{
					AppendToFile = true,
					File = "sigma.log",
					Layout = patternLayout,
					MaxSizeRollBackups = 5,
					MaximumFileSize = "15MB",
					RollingStyle = RollingFileAppender.RollingMode.Size,
					StaticLogFileName = true
				};
				roller.ActivateOptions();

				hierarchy.Root.AddAppender(roller);

				hierarchy.Root.Level = Level.Debug;
				hierarchy.Configured = true;
			}
		}

		/// <summary>
		/// Create an environment with a certain name.
		/// </summary>
		/// <param name="environmentName"></param>
		/// <returns>A new environment with the given name.</returns>
		public static SigmaEnvironment Create(string environmentName)
		{
			if (Exists(environmentName))
			{
				throw new ArgumentException($"Cannot create environment, environment {environmentName} already exists.");
			}

			SigmaEnvironment environment = new SigmaEnvironment(environmentName);

			//do environment initialisation and registration

			ActiveSigmaEnvironments.Set(environmentName, environment);

			ClazzLogger.Info($"Created and registered sigma environment \"{environmentName}\".");

			return environment;
		}

		/// <summary>
		/// Get environment if it already exists, create and return new one if it does not. 
		/// </summary>
		/// <param name="environmentName"></param>
		/// <returns>A new environment with the given name or the environment already associated with the name.</returns>
		public static SigmaEnvironment GetOrCreate(string environmentName)
		{
			return Exists(environmentName) ? Get(environmentName) : Create(environmentName);
		}

		/// <summary>
		/// Gets an environment with a given name, if previously created (null otherwise).
		/// </summary>
		/// <param name="environmentName">The environment name.</param>
		/// <returns>The existing with the given name or null.</returns>
		public static SigmaEnvironment Get(string environmentName)
		{
			return ActiveSigmaEnvironments.Get<SigmaEnvironment>(environmentName);
		}

		/// <summary>
		/// Checks whether an environment exists with the given name.
		/// </summary>
		/// <param name="environmentName">The environment name.</param>
		/// <returns>A boolean indicating if an environment with the given name exists.</returns>
		public static bool Exists(string environmentName)
		{
			return ActiveSigmaEnvironments.ContainsKey(environmentName);
		}

		/// <summary>
		/// Removes an environment with a given name.
		/// </summary>
		/// <param name="environmentName">The environment name.</param>
		public static void Remove(string environmentName)
		{
			ActiveSigmaEnvironments.Remove(environmentName);
		}

		/// <summary>
		/// Removes all active environments.
		/// </summary>
		public static void Clear()
		{
			ActiveSigmaEnvironments.Clear();
		}

		#endregion static
	}
}