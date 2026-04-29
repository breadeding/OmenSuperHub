# DiskInfoToolkit
[![GitHub license](https://img.shields.io/github/license/blacktempel/diskinfotoolkit?label=License)](https://github.com/blacktempel/diskinfotoolkit/blob/master/LICENSE)
[![Build master](https://github.com/Blacktempel/DiskInfoToolkit/actions/workflows/master.yml/badge.svg)](https://github.com/Blacktempel/DiskInfoToolkit/actions/workflows/master.yml)
[![Nuget](https://img.shields.io/nuget/v/DiskInfoToolkit?label=NuGet)](https://www.nuget.org/packages/DiskInfoToolkit/)
[![Nuget](https://img.shields.io/nuget/dt/DiskInfoToolkit?label=NuGet-Downloads)](https://www.nuget.org/packages/DiskInfoToolkit/)

A toolkit for Storage Device informations. Primarily used for reading [S.M.A.R.T.](https://en.wikipedia.org/wiki/Self-Monitoring,_Analysis_and_Reporting_Technology) data from storage devices.

## Project overview
| Project | .NET Version[s] |
| --- | --- |
| **[DiskInfoToolkit](https://github.com/Blacktempel/DiskInfoToolkit/tree/master/DiskInfoToolkit)** <br/> This library reads detailed information from various types of storage devices - including NVMe, SSD, HDD and USB drives. <br/> It provides a high level API to read device data, [SMART attributes](https://en.wikipedia.org/wiki/Self-Monitoring,_Analysis_and_Reporting_Technology), Partitions and other hardware details directly from the system. | .NET Framework 4.7.2 & 4.8.1 <br/> .NET Standard 2.0 <br/> .NET 8, 9 and 10 |
| **[ConsoleOutputTest](https://github.com/Blacktempel/DiskInfoToolkit/tree/master/ConsoleOutputTest)** <br/> Example Application to show how some library functionality can be used. | .NET 8 |
| **[DiskInfoViewer](https://github.com/Blacktempel/DiskInfoToolkit/tree/master/DiskInfoViewer)** <br/> Visualization of detected storage devices on your system. <br/> This supports adding / removing storage devices and updates data. <br/> UI is built using [Avalonia UI.](https://avaloniaui.net/) | .NET 8 |

## What platforms are supported ?
For the moment we only support Windows.<br/>
We are looking into supporting Linux later on.

## Where can I download it ?
You can download the latest release [from here.](https://github.com/Blacktempel/DiskInfoToolkit/releases)

## How can I help improve the project ?
Feel free to give feedback and contribute to our project !<br/>
Pull requests are welcome. Please include as much information as possible.

## Developer information
**Integrate the library in your own application**

**Sample code**
```C#
static class Program
{
    static void Main(string[] args)
    {
        //You can enable logging and set level, if you need logging output
        Logger.Instance.IsEnabled = true;
        Logger.Instance.LogLevel = LogLevel.Trace;

        //Reload storage devices
        StorageManager.ReloadStorages();

        //Go through all devices
        foreach (var storage in StorageManager.Storages)
        {
            //Output Model of storage device
            Console.WriteLine($"Detected storage device '{storage.Model}'.");
        }

        //Register change event
        StorageManager.StoragesChanged += DevicesChanged;

        var secondsToWait = 10;

        //Wait for specified amount of time and listen to device changes
        Console.WriteLine($"Waiting {secondsToWait} seconds for device changes.");
        Thread.Sleep(TimeSpan.FromSeconds(secondsToWait));

        //Unregister change event
        StorageManager.StoragesChanged -= DevicesChanged;

        //Save log file to current directory, if you enabled logging output
        Logger.Instance.SaveToFile("Log.txt", false);

        //All done
        Console.WriteLine("Press enter to exit...");
        Console.ReadLine();
    }

    static void DevicesChanged(StoragesChangedEventArgs e)
    {
        //Devices have changed, log event information
        Console.WriteLine($"Eventhandler {nameof(StorageManager.StoragesChanged)} - {e.StorageChangeIdentifier} => {e.Storage.Model}.");

        //Simple output
        switch (e.StorageChangeIdentifier)
        {
            case StorageChangeIdentifier.Added:
                Console.WriteLine($"Added: '{e.Storage.Model}'");
                break;
            case StorageChangeIdentifier.Removed:
                Console.WriteLine($"Removed: '{e.Storage.Model}'");
                break;
        }
    }
}
```

## License
DiskInfoToolkit is free and open source software licensed under MPL 2.0.<br/>
You can use it in private and commercial projects.<br/>
Keep in mind that you must include a copy of the license in your project.

Some of our core code is based on [CrystalDiskInfo.](https://github.com/hiyohiyo/CrystalDiskInfo)