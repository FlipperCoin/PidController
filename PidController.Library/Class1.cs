using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace PidController.Library
{

    public interface IGain
    {
        double In(double input);
    }

    public class AggregateGain : IGain
    {
        private readonly IEnumerable<IGain> _gainControllers;

        public AggregateGain(IEnumerable<IGain> gainControllers)
        {
            _gainControllers = gainControllers;
        }

        public double In(double input)
        {
            return _gainControllers
                .Select(g => g.In(input))
                .Sum();
        }
    }

    public class PidController : IGain
    {
        private AggregateGain _pidGainAggregate;

        public PidController(IFactory<IGain> pGainFactory, IFactory<IGain> iGainFactory, IFactory<IGain> dGainFactory)
        {
            _pidGainAggregate = new AggregateGain(new[] { pGainFactory.Create(), iGainFactory.Create(), dGainFactory.Create() });
        }

        public double In(double input)
        {
            return _pidGainAggregate.In(input);
        }
    }

    public interface IFactory<T>
    {
        T Create();
    }

    public class ProportionalGain : IGain
    {
        private readonly double _k;

        public ProportionalGain(double k)
        {
            _k = k;
        }
        public double In(double input)
        {
            return _k * input;
        }
    }

    public class RollingQueue<T> : Queue<T>
    {
        private readonly int _size;

        public RollingQueue(int size) : base(size)
        {
            _size = size;
        }

        public new void Enqueue(T obj)
        {
            while (base.Count > (_size - 1))
            {
                base.Dequeue();
            }

            base.Enqueue(obj);
        }
    }

    public class DerivativeGain : IGain
    {
        private readonly double _k;
        // for n as the window, contains n-1 items to add to the new item (for a total of n items) to do the d calc
        private readonly RollingQueue<(double, DateTime)> _inputs;

        public DerivativeGain(double k, int window)
        {
            _k = k;
            _inputs = new RollingQueue<(double, DateTime)>(window - 1);
        }
        public double In(double input)
        {
            var timedInput = (input, DateTime.Now);

            if (_inputs.Count == 0)
            {
                _inputs.Enqueue(timedInput);
                return 0;
            }

            var d = _inputs
                .Zip(_inputs.Skip(1).Append(timedInput), ((double val, DateTime time) first, (double val, DateTime time) second)  =>
                    (first.val - second.val) / (first.time - second.time).TotalSeconds)
                .Average();

            _inputs.Enqueue(timedInput);

            return _k * d;
        }
    }

    public class IntegralGain : IGain
    {
        private readonly double _k;
        private RollingQueue<double> _inputs;

        public IntegralGain(double k, int window)
        {
            _k = k;
            _inputs = new RollingQueue<double>(window);
        }
        public double In(double input)
        {
            _inputs.Enqueue(input);

            if (!(_inputs.Count > 1)) return 0;

            return _k * _inputs.Sum();
        }
    }

    public class Factory<T> : IFactory<T>
    {
        private readonly Func<T> _createFunction;

        public Factory(Func<T> createFunction)
        {
            _createFunction = createFunction;
        }

        public T Create()
        {
            return _createFunction();
        }
    }

    public class TvcMountController
    {
        private const double MAX_ANGLE = 35;

        private readonly ITimeProvider _timeProvider;
        private readonly ReaderWriterLockSlim _angleHistoryLock;

        private readonly List<(double Angle, TimeSpan Time)> _angleHistory;
        public List<(double Angle, TimeSpan Time)> AngleHistory
        {
            get
            {
                _angleHistoryLock.EnterReadLock();
                var cpy = _angleHistory.ToList();
                _angleHistoryLock.ExitReadLock();
                return cpy;
            }
        }

        private readonly List<(double Angle, TimeSpan Delta)> _angleDeltaHistory;
        public List<(double Angle, TimeSpan Delta)> AngleDeltaHistory
        {
            get
            {
                _angleHistoryLock.EnterReadLock();
                var cpy = _angleDeltaHistory.ToList();
                var (lastAngle, lastAngleTime) = _angleHistory[^1];
                _angleHistoryLock.ExitReadLock();
                cpy.Add((lastAngle, _timeProvider.Time - lastAngleTime));
                return cpy;
            }
        }

        public double MountAngle
        {
            get => _angleHistory[^1].Angle;
            set
            {
                if (value > MAX_ANGLE) value = MAX_ANGLE;
                else if (value < -MAX_ANGLE) value = -MAX_ANGLE;

                var epsilon = value - _angleHistory[^1].Angle;
                if (epsilon < 1 && epsilon > -1) return;

                var t = _timeProvider.Time;
                _angleHistoryLock.EnterWriteLock();
                _angleDeltaHistory.Add((_angleHistory[^1].Angle, t - _angleHistory[^1].Time));
                _angleHistory.Add((value, t));
                _angleHistoryLock.ExitWriteLock();
            }
        }

        public TvcMountController(ITimeProvider timeProvider, double startAngle = 0)
        {
            _timeProvider = timeProvider;
            _angleHistoryLock = new ReaderWriterLockSlim();

            _angleHistory = new List<(double Angle, TimeSpan Time)> { (startAngle, TimeSpan.Zero) };
            _angleDeltaHistory = new List<(double Angle, TimeSpan Delta)>();
        }
    }

    public interface ITimeProvider
    {
        TimeSpan Time { get; }
    }

    public class TimeProvider : ITimeProvider
    {
        private DateTime _startTime;
        public TimeSpan Time => DateTime.Now - _startTime;

        public void Start()
        {
            _startTime = DateTime.Now;
        }
    }

    public class Rocket
    {
        private readonly TvcMountController _tvc;
        private readonly double _height;
        private readonly double _mass;
        private readonly double _thrust;
        private double _momentOfInertia;
        private readonly double _startAngle;
        private readonly double _startAngleRate;

        public double Angle =>
            _tvc.AngleDeltaHistory.Aggregate(
                (_startAngle, _startAngleRate),
                ((double rocketAngle, double omega) t0vals, (double angle, TimeSpan delta) tv) =>
                {
                    var domegadt = -((_thrust * Math.Sin((Math.PI / 180) * tv.angle)) / _momentOfInertia);
                    return (
                        t0vals.rocketAngle + (t0vals.omega * tv.delta.TotalSeconds) + 0.5 * domegadt * Math.Pow(tv.delta.TotalSeconds, 2),
                        t0vals.omega + domegadt * tv.delta.TotalSeconds
                    );
                }
            ).Item1;

        public Rocket(TvcMountController tvc, double height, double mass, double thrust, double startAngle = 0, double startAngleRate = 0)
        {
            _tvc = tvc;
            _height = height;
            _mass = mass;
            _thrust = thrust;
            _momentOfInertia = (1d / 12d) * mass * Math.Pow(height, 2);
            _startAngle = startAngle;
            _startAngleRate = startAngleRate;
        }
    }

}
