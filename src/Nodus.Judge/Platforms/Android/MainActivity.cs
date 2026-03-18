using System;
using System.Linq;

#pragma warning disable CA1416
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android;

namespace Nodus.Judge;

[Activity(Theme = "@style/Nodus.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private bool _blePermissionsRequested;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        TryRequestBlePermissions();
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        // ZXing requests CAMERA on demand. If that request is in progress, do not race it with BLE.
        if (permissions.Any(p => string.Equals(p, Manifest.Permission.Camera, StringComparison.Ordinal)))
        {
            TryRequestBlePermissions();
        }
    }

    private void TryRequestBlePermissions()
    {
        if (_blePermissionsRequested)
        {
            return;
        }

        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            var cameraGranted = CheckSelfPermission(Manifest.Permission.Camera) == Permission.Granted;
            if (!cameraGranted)
            {
                // Let camera flow complete first to avoid Android's one-request-at-a-time rejection.
                return;
            }
        }

        string[] missingPermissions;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
        {
            missingPermissions =
            new[]
            {
                Manifest.Permission.BluetoothScan,
                Manifest.Permission.BluetoothConnect,
                Manifest.Permission.BluetoothAdvertise,
            }
            .Where(permission => CheckSelfPermission(permission) != Permission.Granted)
            .ToArray();
        }
        else
        {
            missingPermissions =
            new[]
            {
                Manifest.Permission.AccessFineLocation,
            }
            .Where(permission => CheckSelfPermission(permission) != Permission.Granted)
            .ToArray();
        }

        if (missingPermissions.Length == 0)
        {
            _blePermissionsRequested = true;
            return;
        }

        RequestPermissions(missingPermissions, requestCode: 1001);
        _blePermissionsRequested = true;
    }
}
