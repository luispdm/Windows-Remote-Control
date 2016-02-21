# Windows Remote Control

**NOTE: the software has a pure educational purpose**

## Description

Project developed for the **Concurrent Programming** (**Programmazione di Sistema**) course at the **Politecnico di Torino** in the 2013/2014.

The application is a _Client-Server_ like. It is possible to share three resources: keyboard, mouse and clipboard between several Windows Machines (tested on _Windows 7_ and _Windows 8_). It works only in **LAN** (since desktop sharing is not provided). Clipboard sharing is bidirectional

Resources sharing is possible _one at time_, so a _Thread synchronization_ mechanism was necessary.

The Server needs to be installed on the machine that _receives_ the input; while the Client, the one who takes control, could be installed over different computers. Once the server is installed, you need to type in **IP address, port and password** to start listening. If the client wants to control that server, the credentials need to match.

Once the connection is estabilished, the server window automatically goes into try icon and the user can realize he's been controlled thanks to a thin red square wrapping the display.

It is possible to copy and send three kind of files:

* **Image (bitmap encoding)**
* **File (or directory)**
* **Text**

When copy is performed, before hitting the network, the _object_ is compressed into a _zip_ and stored into a temporary folder. A 3rd-party library was used called _ICSharpCode_. When the file is sent, the counterpart will unzip and then paste it into the clipboard.

The instructions of use (like particular key combinations) are in the main window of the program; unfortunately, like code's comments, they are all in Italian.

The two _.exe_ can be found here under the root (_FinalClient_ and _FinalServer_).

**Topics: C#, XAML, Visual Studio 2013, .NET, WPF, Win32 functions.**

**NOTE: no security mechanism (like a crypto stream) implemented, the password could be easily sniffed over the network**.