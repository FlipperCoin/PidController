using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PidController.Library;

namespace PidController.Console
{
    class Program
    {
        static void Main(string[] args)
        {
            const double kp = -1;
            const double ki = 0; // don't wanna use i gain for now
            const double kd = -0.8;
            
            var pidController = new Library.PidController(
                new Factory<IGain>(() => new ProportionalGain(kp)), 
                new Factory<IGain>(() => new IntegralGain(ki, 5)), 
                new Factory<IGain>(() => new DerivativeGain(kd, 5))
            );

            var tvcMountStartAngle = 0d;
            var rocketStartAngle = 40;
            var rocketStartAngleRate = 40;
            
            var timeProvider = new TimeProvider();
            var tvcMount = new TvcMountController(timeProvider, tvcMountStartAngle);

            var rocketMass = 0.5d;
            var height = 1;
            var thrust = 7;
            
            var rocket = new Rocket(tvcMount, height, rocketMass, thrust, rocketStartAngle, rocketStartAngleRate);

            timeProvider.Start();
            
            Task.Run(() =>
            {
                var sp = 0d;
                while (true)
                {
                    var pv = rocket.Angle;
                    var err = sp - pv;
                    var cv = pidController.In(err);
                    tvcMount.MountAngle = cv;
                    Thread.Sleep(50);
                }
            });

            while (true)
            {
                System.Console.WriteLine($"Time: {timeProvider.Time}, Rocket Angle: {rocket.Angle}, TVC Mount Angle: {tvcMount.MountAngle}");
                Thread.Sleep(500);
            }
        }
    }
}