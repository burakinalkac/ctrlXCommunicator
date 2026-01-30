ctrlX Data Layer Explorer
A lightweight, WPF-based diagnostic tool for Bosch Rexroth ctrlX AUTOMATION systems.

---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

This application is a versatile communication tool designed to interface with the Bosch Rexroth ctrlX Data Layer. It allows automation engineers and developers to browse the internal node structure of a ctrlX CORE, monitor live values in real-time, and perform write operations across various data types (Boolean, Integers, Floats, Strings).

Built with C# and WPF, it leverages the official comm.datalayer SDK to provide a high-performance, asynchronous experience for debugging and commissioning industrial IoT applications.

---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

Key Features:
Dynamic Connectivity: Real-time connection management via user-defined IP addresses.

Node Browser: Hierarchical exploration of the entire Data Layer tree with "Back" and "Refresh" capabilities.

Live Monitoring: Background polling (async) to track variable changes with sub-second latency.
Smart Write Panel: Context-aware UI that automatically adapts input controls (ComboBox for Booleans, TextBoxes for numeric/string types) based on the node's DLR_VARIANT_TYPE.

Health Monitoring: Visual heartbeat/LED indicator to track the communication status with the ctrlX CORE.

Asynchronous Architecture: Utilizes CancellationToken and Task.Run to ensure the UI remains responsive during heavy network operations.

---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

Technical Stack:
Language: C# 10.0+

---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

Framework:
.NET / WPF

---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

Communication:

TCP/IP

Bosch Rexroth ctrlX Data Layer SDK

---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

Architecture: 
Event-driven UI with Asynchronous Background Workers

---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

Getting Started:
Ensure you have the ctrlX Data Layer SDK installed in your environment.

---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

Clone this repository:

Open the solution in Visual Studio.

Build and run.

Enter your ctrlX CORE IP address (default: 192.168.0.11) and hit Connect.

---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

Use Cases:

PLC Debugging: Rapidly check I/O states or variable values without opening PLC Engineering.

System Integration: Verify REST or MQTT data paths within the Data Layer.

Testing: Simulate sensor data by writing values directly to the provider nodes.
