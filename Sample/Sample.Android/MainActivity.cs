using System;

using Android.App;
using Android.Content.PM;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;
using DelsysAPI.Pipelines;
using DelsysAPI.Contracts;
using System.Collections.Generic;
using DelsysAPI.DelsysDevices;
using Android.Support.V4.App;
using Android;
using DelsysAPI.Events;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using DelsysAPI.Utils.TrignoBt;
using DelsysAPI.Utils;
using DelsysAPI.Configurations.DataSource;
using DelsysAPI.Configurations;
using DelsysAPI.Configurations.Component;
using DelsysAPI.Transforms;
using DelsysAPI.Channels.Transform;
using System.Reflection;

namespace Sample.Droid
{

    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme.NoActionBar", MainLauncher = true)]
    public class MainActivity : Android.Support.V7.App.AppCompatActivity
    {
        // Defining buttons for UI
        public Button ScanButton;
        public Button ArmButton;
        public Button StreamButton;
        public Button StopButton;


        Pipeline BTPipeline;
        ITransformManager TransformManager;
        /// <summary>
        /// If there are no device filters, the central will connect to every Avanti sensor
        /// it detects.
        /// </summary>
        string[] DeviceFilters = new string[]
        {
        };

        /// <summary>
        /// Data structure for recording every channel of data.
        /// </summary>
        List<List<double>> Data = new List<List<double>>();

        IDelsysDevice DeviceSource = null;

        int TotalLostPackets = 0;
        int TotalDataPoints = 0;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            SetContentView(Resource.Layout.activity_main);

            Android.Support.V7.Widget.Toolbar toolbar = FindViewById<Android.Support.V7.Widget.Toolbar>(Resource.Id.toolbar);
            SetSupportActionBar(toolbar);

            ScanButton = FindViewById<Button>(Resource.Id.btn_Scan);
            ScanButton.Click += clk_Scan;

            ArmButton = FindViewById<Button>(Resource.Id.btn_Arm);
            ArmButton.Click += clk_Arm;

            StreamButton = FindViewById<Button>(Resource.Id.btn_Stream);
            StreamButton.Click += clk_Start;

            StopButton = FindViewById<Button>(Resource.Id.btn_Stop);
            StopButton.Click += clk_Stop;

            StreamButton.Enabled = false;
            ArmButton.Enabled = false;
            ScanButton.Enabled = true;
            StopButton.Enabled = false;

            CheckAppPermissions();

            InitializeDataSource();
        }

        public override bool OnCreateOptionsMenu(IMenu menu)
        {
            MenuInflater.Inflate(Resource.Menu.menu_main, menu);
            return true;
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            int id = item.ItemId;
            if (id == Resource.Id.action_settings)
            {
                return true;
            }

            return base.OnOptionsItemSelected(item);
        }

        #region Button Events (Scan, Start, and Stop)

        // Check and request permissions
        private void CheckAppPermissions()
        {
            if ((int)Build.VERSION.SdkInt < 23)
            {
                return;
            }
            else
            {
                if (PackageManager.CheckPermission(Manifest.Permission.ReadExternalStorage, PackageName) != Permission.Granted
                    && PackageManager.CheckPermission(Manifest.Permission.WriteExternalStorage, PackageName) != Permission.Granted)
                {
                    var permissions = new string[] { Manifest.Permission.ReadExternalStorage, Manifest.Permission.WriteExternalStorage, Manifest.Permission.AccessCoarseLocation, Manifest.Permission.AccessFineLocation };
                    RequestPermissions(permissions, 1);
                }
            }
        }

        public void clk_Start(object sender, EventArgs e)
        {
            // The pipeline must be reconfigured before it can be started again.
            ConfigurePipeline();
            BTPipeline.Start();
            StreamButton.Enabled = false;
            ArmButton.Enabled = false;
            ScanButton.Enabled = false;
            StopButton.Enabled = true;
        }

        public void clk_Arm(object sender, EventArgs e)
        {
            // Select every component we found and didn't filter out.
            foreach (var component in BTPipeline.TrignoBtManager.Components)
            {
                BTPipeline.TrignoBtManager.SelectComponentAsync(component);
            }

            ConfigurePipeline();
            StreamButton.Enabled = true;
            ArmButton.Enabled = false;
            ScanButton.Enabled = true;
            StopButton.Enabled = false;
        }

        public void clk_Scan(object sender, EventArgs e)
        {
            StreamButton.Enabled = false;
            ArmButton.Enabled = false;
            ScanButton.Enabled = false;
            StopButton.Enabled = false;

            BTPipeline.Scan();
        }

        public void clk_Stop(object sender, EventArgs e)
        {
            BTPipeline.Stop();
            StreamButton.Enabled = true;
            ArmButton.Enabled = false;
            ScanButton.Enabled = false;
            StopButton.Enabled = false;
        }

        #endregion

        #region Initialization

        public void InitializeDataSource()
        {
            int SUCCESS = 0;
            ActivityCompat.RequestPermissions(this, new String[] { Manifest.Permission.AccessCoarseLocation }, SUCCESS);

            // Load your license and key files
            // This tutorial assumes you have them contained in embedded resources named PublicKey.lic and License.lic, as part of
            // a solution with an output executable called BasicExample.
            var assembly = Assembly.GetExecutingAssembly();
            string key;
            using (Stream stream = assembly.GetManifestResourceStream("Sample.Droid.PublicKey.lic"))
            {
                StreamReader sr = new StreamReader(stream);
                key = sr.ReadLine();
            }
            string lic;
            using (Stream stream = assembly.GetManifestResourceStream("Sample.Droid.License.lic"))
            {
                StreamReader sr = new StreamReader(stream);
                lic = sr.ReadToEnd();
            }

            var deviceSourceCreator = new DelsysAPI.Android.DeviceSourcePortable(key, lic);
            deviceSourceCreator.SetDebugOutputStream(Console.WriteLine);
            DeviceSource = deviceSourceCreator.GetDataSource(SourceType.TRIGNO_BT);
            DeviceSource.Key = key;
            DeviceSource.License = lic;
            LoadDataSource(DeviceSource);

            // The API uses a factory method to create the data source of your application.
            // This creates the factory method, which will then give the data source for your platform.
            // In this case the platform is RF.
            //var deviceSourceCreator = new DelsysAPI.Android.DeviceSourcePortable(key, lic);
            // Sets the output stream for debugging information from the API. This could be a file stream,
            // but in this example we simply use the Console.WriteLine output stream.
            deviceSourceCreator.SetDebugOutputStream(Console.WriteLine);
            // Here is where we tell the factory method what type of data source we want to receive,
            // which we then set a reference to for future use.
            DeviceSource = deviceSourceCreator.GetDataSource(SourceType.TRIGNO_BT);
            // Here we use the key and license we previously loaded.
            DeviceSource.Key = key;
            DeviceSource.License = lic;
            // Create a reference to the first Pipeline (which was generated by the factory method above)
            // for easier access to various objects within the API.
            BTPipeline = PipelineController.Instance.PipelineIds[0];
            TransformManager = PipelineController.Instance.PipelineIds[0].TransformManager;

            // Just setting up some of the necessary callbacks from the API.
            BTPipeline.CollectionStarted += CollectionStarted;
            BTPipeline.CollectionDataReady += CollectionDataReady;
            BTPipeline.CollectionComplete += CollectionComplete;
            BTPipeline.TrignoBtManager.ComponentAdded += ComponentAdded;
            BTPipeline.TrignoBtManager.ComponentLost += ComponentLost;
            BTPipeline.TrignoBtManager.ComponentRemoved += ComponentRemoved;
            BTPipeline.TrignoBtManager.ComponentScanComplete += ComponentScanComplete;

            // The component manager is how you reference specific / individual sensors so creating 
            // a reference to it will shorten a lot of calls.
            var ComponentManager = PipelineController.Instance.PipelineIds[0].TrignoBtManager;
        }

        public void LoadDataSource(IDelsysDevice ds)
        {
            PipelineController.Instance.AddPipeline(ds);

            BTPipeline = PipelineController.Instance.PipelineIds[0];
            TransformManager = PipelineController.Instance.PipelineIds[0].TransformManager;

            // Device Filters allow you to specify which sensors to connect to
            foreach (var filter in DeviceFilters)
            {
                BTPipeline.TrignoBtManager.AddDeviceIDFilter(filter);
            }

            BTPipeline.CollectionComplete += CollectionComplete;
            BTPipeline.CollectionStarted += CollectionStarted;
            BTPipeline.CollectionDataReady += CollectionDataReady;

            BTPipeline.TrignoBtManager.ComponentScanComplete += ComponentScanComplete;
        }

        #endregion

        #region Componenet Callbacks -- Component Added, Scan Complete

        private void ComponentScanComplete(object sender, DelsysAPI.Events.ComponentScanCompletedEventArgs e)
        {
            StreamButton.Enabled = false;
            ArmButton.Enabled = true;
            ScanButton.Enabled = true;
            StopButton.Enabled = false;
        }

        #endregion

        #region Component Callbacks -- Found, Lost, Removed

        public void ComponentAdded(object sender, ComponentAddedEventArgs e)
        {
        }

        public void ComponentLost(object sender, ComponentLostEventArgs e)
        {

        }

        public void ComponentRemoved(object sender, ComponentRemovedEventArgs e)
        {

        }

        #endregion


        #region Collection Callbacks -- Data Ready, Colleciton Started, and Collection Complete
        public void CollectionDataReady(object sender, ComponentDataReadyEventArgs e)
        {
            int lostPackets = 0;
            int dataPoints = 0;

            // Check each data point for if it was lost or not, and add it to the sum totals.
            for (int j = 0; j < e.Data.Count(); j++)
            {
                var channelData = e.Data[j];
                Data[j].AddRange(channelData.Data);
                dataPoints += channelData.Data.Count;
                for (int i = 0; i < channelData.Data.Count; i++)
                {
                    if (e.Data[0].IsLostData[i])
                    {
                        lostPackets++;
                    }
                }
            }
            TotalLostPackets += lostPackets;
            TotalDataPoints += dataPoints;
        }

        private void CollectionStarted(object sender, DelsysAPI.Events.CollectionStartedEvent e)
        {
            var comps = PipelineController.Instance.PipelineIds[0].TrignoBtManager.Components;

            // Refresh the counters for display.
            TotalDataPoints = 0;
            TotalLostPackets = 0;

            // Recreate the list of data channels for recording
            int totalChannels = 0;
            for (int i = 0; i < comps.Count; i++)
            {
                for (int j = 0; j < comps[i].BtChannels.Count; j++)
                {
                    if (Data.Count <= totalChannels)
                    {         
                        Data.Add(new List<double>());
                    }
                    else
                    {
                        Data[totalChannels] = new List<double>();
                    }
                    totalChannels++;
                }
            }
            Task.Factory.StartNew(() => {
                Stopwatch batteryUpdateTimer = new Stopwatch();
                batteryUpdateTimer.Start();
                while (BTPipeline.CurrentState == Pipeline.ProcessState.Running)
                {
                    if (batteryUpdateTimer.ElapsedMilliseconds >= 500)
                    {
                        foreach (var comp in BTPipeline.TrignoBtManager.Components)
                        {
                            if (comp == null)
                                continue;
                            Console.WriteLine("Sensor {0}: {1}% Charge", comp.Properties.SerialNumber, BTPipeline.TrignoBtManager.QueryBatteryComponentAsync(comp).Result);
                        }
                        batteryUpdateTimer.Restart();
                    }
                }
            });
        }

        private void CollectionComplete(object sender, DelsysAPI.Events.CollectionCompleteEvent e)
        {
            string path = Path.Combine(Android.OS.Environment.ExternalStorageDirectory.AbsolutePath, Android.OS.Environment.DirectoryDownloads);
            for (int i = 0; i < Data.Count; i++)
            {
                string filename = Path.Combine(path, "sensor" + i + "_data.csv");
                using (StreamWriter channelOutputFile = new StreamWriter(filename, true))
                {
                    foreach (var pt in Data[i])
                    {
                        channelOutputFile.WriteLine(pt.ToString());
                    }
                }
            }
            // If you do not disarm the pipeline, then upon stopping you may begin streaming again.
            BTPipeline.DisarmPipeline().Wait();
        }

        #endregion

        #region Data Collection Configuration

        /// <summary>
        /// Configures the input and output of the pipeline.
        /// </summary>
        /// <returns></returns>
        private bool CallbacksAdded = false;
        private bool ConfigurePipeline()
        {
            if (PipelineController.Instance.PipelineIds[0].CurrentState == Pipeline.ProcessState.OutputsConfigured || PipelineController.Instance.PipelineIds[0].CurrentState == Pipeline.ProcessState.Armed)
            {
                PipelineController.Instance.PipelineIds[0].DisarmPipeline();
            }
            if (PipelineController.Instance.PipelineIds[0].CurrentState == Pipeline.ProcessState.Running)
            {
                // If it is running; stop the task and then disarm the pipeline
                PipelineController.Instance.PipelineIds[0].Stop();
            }

            // Arm everything (should be in "Connected" State)
            var state = PipelineController.Instance.PipelineIds[0].CurrentState;

            if (CallbacksAdded)
            {
                BTPipeline.TrignoBtManager.ComponentAdded -= ComponentAdded;
                BTPipeline.TrignoBtManager.ComponentLost -= ComponentLost;
                BTPipeline.TrignoBtManager.ComponentRemoved -= ComponentRemoved;
            }

            BTPipeline.TrignoBtManager.ComponentAdded += ComponentAdded;
            BTPipeline.TrignoBtManager.ComponentLost += ComponentLost;
            BTPipeline.TrignoBtManager.ComponentRemoved += ComponentRemoved;
            CallbacksAdded = true;

            PipelineController.Instance.PipelineIds[0].TrignoBtManager.Configuration = new TrignoBTConfig() { EOS = EmgOrSimulate.EMG };

            var inputConfiguration = new BTDsConfig();
            inputConfiguration.NumberOfSensors = BTPipeline.TrignoBtManager.Components.Count;

            foreach (var somecomp in BTPipeline.TrignoBtManager.Components.Where(x => x.State == SelectionState.Allocated))
            {
                string selectedMode = "EMG RMS";
                //Synchronize to the UI thread and check if the mode textbox value exists in the
                // available sample modes for the sensor.

                somecomp.SensorConfiguration.SelectSampleMode(selectedMode);

                if (somecomp.SensorConfiguration == null)
                {
                    return false;
                }
            }

            PipelineController.Instance.PipelineIds[0].ApplyInputConfigurations(inputConfiguration);
            var transformTopology = GenerateTransforms();//For multi Sensors
            PipelineController.Instance.PipelineIds[0].ApplyOutputConfigurations(transformTopology);
            PipelineController.Instance.PipelineIds[0].RunTime = Double.MaxValue;


            return true;
        }

        /// <summary>
        /// Generates the Raw Data transform that produces our program's output.
        /// </summary>
        /// <returns>A transform configuration to be given to the API pipeline.</returns>
        public OutputConfig GenerateTransforms()
        {
            // Clear the previous transforms should they exist.
            TransformManager.TransformList.Clear();

            int channelNumber = 0;
            // Obtain the number of channels based on our sensors and their mode.
            for (int i = 0; i < BTPipeline.TrignoBtManager.Components.Count; i++)
            {
                if (BTPipeline.TrignoBtManager.Components[i].State == SelectionState.Allocated)
                {
                    var tmp = BTPipeline.TrignoBtManager.Components[i];

                    BTCompConfig someconfig = tmp.SensorConfiguration as BTCompConfig;
                    if (someconfig.IsComponentAvailable())
                    {
                        channelNumber += BTPipeline.TrignoBtManager.Components[i].BtChannels.Count;
                    }

                }
            }

            // Create the raw data transform, with an input and output channel for every
            // channel that exists in our setup. This transform applies the scaling to the raw
            // data from the sensor.
            var rawDataTransform = new TransformRawData(channelNumber, channelNumber);
            PipelineController.Instance.PipelineIds[0].TransformManager.AddTransform(rawDataTransform);

            // The output configuration for the API to use.
            var outconfig = new OutputConfig();
            outconfig.NumChannels = channelNumber;

            int channelIndex = 0;
            for (int i = 0; i < BTPipeline.TrignoBtManager.Components.Count; i++)
            {
                if (BTPipeline.TrignoBtManager.Components[i].State == SelectionState.Allocated)
                {
                    BTCompConfig someconfig = BTPipeline.TrignoBtManager.Components[i].SensorConfiguration as BTCompConfig;
                    if (someconfig.IsComponentAvailable())
                    {
                        // For every channel in every sensor, we gather its sampling information (rate, interval, units) and create a
                        // channel transform (an abstract channel used by transforms) from it. We then add the actual component's channel
                        // as an input channel, and the channel transform as an output. 
                        // Finally, we map the channel counter and the output channel. This mapping is what determines the channel order in
                        // the CollectionDataReady callback function.
                        for (int k = 0; k < BTPipeline.TrignoBtManager.Components[i].BtChannels.Count; k++)
                        {
                            var chin = BTPipeline.TrignoBtManager.Components[i].BtChannels[k];
                            var chout = new ChannelTransform(chin.FrameInterval, chin.SamplesPerFrame, BTPipeline.TrignoBtManager.Components[i].BtChannels[k].Unit);
                            TransformManager.AddInputChannel(rawDataTransform, chin);
                            TransformManager.AddOutputChannel(rawDataTransform, chout);
                            Guid tmpKey = outconfig.MapOutputChannel(channelIndex, chout);
                            channelIndex++;
                        }
                    }
                }
            }
            return outconfig;
        }

        #endregion
    }
}