using PidController.Library;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PidController.Visualization
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        double _rocketAngle;
        double _tvcMountAngle;
        private TimeProvider _timeProvider;
        TvcMountController _tvcMount;
        Rocket _rocket;
        Library.PidController _pidController;
        DispatcherTimer _timer;

        bool _cancel = false;        

        public MainWindow()
        {
            InitializeComponent();

            Init();
        }

        private void Run()
        {
            _timeProvider.Start();
            _cancel = false;

            Task.Run(() =>
            {
                var sp = -20d;
                while (!_cancel)
                {
                    var pv = _rocket.Angle;
                    _rocketAngle = pv;
                    var err = pv - sp;
                    var cv = _pidController.In(err);
                    _tvcMount.MountAngle = cv;
                    _tvcMountAngle = _tvcMount.MountAngle;
                    Thread.Sleep(50);
                }
            });
        }

        private void Init()
        {
            const double kp = 1;
            const double ki = 0.01;
            const double kd = 0.8;

            _pidController = new Library.PidController(
                new Factory<IGain>(() => new ProportionalGain(kp)),
                new Factory<IGain>(() => new IntegralGain(ki)),
                new Factory<IGain>(() => new DerivativeGain(kd, 5))
            );

            var tvcMountStartAngle = 0d;
            var rocketStartAngle = 40;
            var rocketStartAngleRate = 70;

            _rocketAngle = rocketStartAngle;
            _tvcMountAngle = 0;

            _timeProvider = new TimeProvider();
            _tvcMount = new TvcMountController(_timeProvider, tvcMountStartAngle);

            var rocketMass = 0.5d;
            var height = 1;
            var thrust = 7;

            _rocket = new Rocket(_tvcMount, height, rocketMass, thrust, rocketStartAngle, rocketStartAngleRate);

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(50);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            var nozzleTransform = new TransformGroup();
            nozzleTransform.Children.Add(new RotateTransform(_tvcMountAngle, 21.6, 0));
            nozzleTransform.Children.Add(new ScaleTransform(0.8, 0.8));

            var rocketTransfom = new TransformGroup();
            rocketTransfom.Children.Add(new RotateTransform(_rocketAngle, 22, 104.5));
            
            Nozzle.RenderTransform = nozzleTransform;
            Rocket.RenderTransform = rocketTransfom;
        }

        private void StartStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (StartStopButton.Content.Equals("Start"))
            {
                StartStopButton.Content = "Stop";
                Init();
                Run();
            }
            else
            {
                StartStopButton.Content = "Start";
                _cancel = true;
                _timer?.Stop();
                Init();
            }
        }
    }
}