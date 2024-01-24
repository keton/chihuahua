<!-- Improved compatibility of back to top link: See: https://github.com/othneildrew/Best-README-Template/pull/73 -->
<a name="readme-top"></a>

<!-- PROJECT SHIELDS -->
<!--
*** I'm using markdown "reference style" links for readability.
*** Reference links are enclosed in brackets [ ] instead of parentheses ( ).
*** See the bottom of this document for the declaration of the reference variables
*** for contributors-url, forks-url, etc. This is an optional, concise syntax you may use.
*** https://www.markdownguide.org/basic-syntax/#reference-style-links
-->
[![Contributors][contributors-shield]][contributors-url]
[![Forks][forks-shield]][forks-url]
[![Stargazers][stars-shield]][stars-url]
[![Issues][issues-shield]][issues-url]
[![MIT License][license-shield]][license-url]

<br />
<div align="center">
<h3 align="center">Chihuahua</h3>
<p align="center">
    Simple injector/game launcher for <a href="https://github.com/praydog/UEVR">Universal Unreal Engine VR mod (UEVR)</a>.
    <br />
  </p>
</div>

<!-- TABLE OF CONTENTS -->
<details>
  <summary>Table of Contents</summary>
  <ol>
    <li>
      <a href="#about-the-project">About The Project</a>
      <ul>
        <li><a href="#built-with">Built With</a></li>
      </ul>
    </li>
    <li>
      <a href="#getting-started">Getting Started</a>
      <ul>
        <li><a href="#prerequisites">Prerequisites</a></li>
        <li><a href="#usage">Usage</a></li>
      </ul>
    </li>
    <li><a href="#usage">Usage</a></li>
    <li><a href="#acknowledgments">Acknowledgments</a></li>
  </ol>
</details>

<!-- ABOUT THE PROJECT -->
## About The Project
<a href="https://github.com/praydog/UEVR">Universal Unreal Engine VR mod (UEVR)</a> by [Praydog](https://www.patreon.com/praydog) allows you to play any Unreal Engine 4.8-5.2 game in VR. Thing is setting it up for successful injection is bit convoluted and tricky for non technical users.

Chihuahua aims to solve this, making whole process as easy as dragging and dropping game `.exe` on chihuahua executable. All hard decisions will be handled for you.

Compared to injector bundled with [UEVR](https://www.patreon.com/praydog) Chihuahua:
* makes all the decisions for the user
* automatically downloads and keeps UEVR up to date
* deals with certain things that can make injection fail (choosing right executable, disabling unwanted UE plugins, adding pre injection delay)
* cleans up leftover processes when game exits. This allows cloud saves to upload properly
* aims to provide easy to understand message about what's going on and especially what went wrong
* stays out of your way. In case of successful injection Chihuahua will exit just after the game does
* is oriented around spawning new game process not attaching to an existing one
* can act as a 'panic button' - closing Chihuahua's window will automatically force close the game and do the cleanup
* does not need .Net framework installation to work

<p align="right">(<a href="#readme-top">back to top</a>)</p>

### Built With

* ![.Net8.0][dotnet-badge]
* ![C#][c-sharp-badge]

<p align="right">(<a href="#readme-top">back to top</a>)</p>

<!-- GETTING STARTED -->
## Getting Started

This is an example of how you may start your first UEVR game. 

For highest chance of success pick one of 'works perfectly' titles with good user opinion. To check all that take a look at [Flat2VR Discord](https://discord.com/invite/ZFSCSDe), `#ue-games` channel.
Also keep in mind that we're going to use VR technology called OpenXR which means chihuahua type of injection will work best on Quest 2/3/Pro and Pico 4 over Virtual Desktop.

Other streaming software and HMDs may be compatible but that is currently untested. In such case you may want to try injector bundled with [UEVR](https://github.com/praydog/UEVR).

### Prerequisites

#### Antivirus exception

Make a folder on your PC and add its location to your antivirus exceptions. The way [UEVR mod](https://github.com/praydog/UEVR) works triggers false positives with antivirus software. 
It's up to you to decide if making an exception or pausing antivirus to play a game is ok. Feel free to review all source code here and in [UEVR repo](https://github.com/praydog/UEVR).

#### Download Chihuahua

Latest release is [here](https://github.com/keton/chihuahua/releases/latest/download/chihuahua.zip). Your browser/antivirus may need some reassuring for the download to happen. Unpack contents to the folder you've added to antivirus exceptions.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

<!-- USAGE EXAMPLES -->
## Usage

### Drag and drop game `.exe` on top of `chihuahua.exe`
![drag drop run demo](assets/drag_drop_run.gif)
_Chihuahua's window has been put on top of game for demonstration purposes only_

1. go to your game's launcher i.e. Steam or Epic
1. use 'browse game files' option to open game installation folder and find game executable
1. open folder you've unpacked chihuahua in another window next to it
1. put on your headset and start Virtual Desktop session
1. drag game executable and drop it on top of chihuahua.exe
1. game should start
1. after 10 seconds [UEVR mod](https://github.com/praydog/UEVR) will start and your headset movement will start affecting game camera. It's strongly suggested to click trough intro cinematics and get to the menu or load a save before that happens. Some games don't like being injected on early boot.
1. if it worked - congratulations. Follow official [UEVR docs](https://praydog.github.io/uevr-docs/index.html) to get the most of the mod.
1. if it did not - close the game, there should be a console window waiting with log that indicates what went wrong.
1. in case of 'Fatal Error' message window on top of your game try again. UEVR is still in early stages, some crashes are expected.

### Command line program

Chihuahua is a well behaved console application. Double click `chihuahua.exe` for up to date list of options. 

```
chihuahua "c:\full\path\to\you\game\folder\game.exe" --delay 30
```

#### Options:
* full path to game `.exe` in double quotes. You can get this from your launcher by going to game properties and clicking 'browse local files'.
* `--delay <number of seconds>` - how long to wait between launching the game and starting UEVR injection process
* `--launch-cmd <launcher URI>` - some Epic Store games don't behave well when launched directly from `.exe`. This parameter allows you to specify launcher specific URI on top. 

  Steam URIs look like this: `steam://rungameid/1730590`.

  Epic Store like this: `com.epicgames.launcher://apps/da36711940d4460da57d8d6ad67236a4%3Aa1afcdb1d2344232954be97d4b86b9a8%3Acfbbc006f3ee4ba0bfff3ffa46942f90?action=launch&silent=true`

  To figure out correct value make a game desktop shortcut in your launcher and check shortcut properties.
* `--launch-args "arg1 arg2"` - you can pass arguments to `--launch-cmd` process and in case that is not specified to game itself.
Common use case would be to add `--launch-args "-nohmd -dx11"` to help with game compatibility.

### Game launch shortcut

Keep in mind that you can make a desktop shortcut to `chihuahua.exe`, go to shortcut properties 
and add full path to game executable in double quotes as a parameter. This will make shortcut to launch and inject the game.

### Rai Pal integration (in progress)

[Rai Pal](https://github.com/raicuparta/rai-pal) is an amazing game launcher by [Raicuparta](https://raicuparta.com/).

You can integrate Chihuahua by unpacking it to `%APPDATA%\raicuparta\rai-pal\data\mod-loaders\runnable\mods\chihuahua`.
Then after starting Rai Pal and clicking on installed game there will be new Chihuahua launch button.
![image](https://github.com/keton/chihuahua/assets/2270836/de0205e5-9f87-467f-a5cd-b6fdb6bd5291)

<p align="right">(<a href="#readme-top">back to top</a>)</p>

<!-- ACKNOWLEDGMENTS -->
## Acknowledgments

Please consider supporting [Praydog](https://www.patreon.com/praydog). He's the wizard class programer making UEVR possible. 

And just to avoid any doubt Chihuahua is just a launcher. All magic that happens once your game starts is solely due to epic level of technical achievement that is [UEVR mod](https://github.com/praydog/UEVR).

* [Praydog on Patreon](https://www.patreon.com/praydog)
* [UEVR mod Github](https://github.com/praydog/UEVR)
* [UEVR mod documentation](https://uevr.io)
* [Rai Pal](https://github.com/raicuparta/rai-pal) game launcher by [Raicuparta](https://raicuparta.com/)
* [Flat2VR Discord](https://discord.com/invite/ZFSCSDe)

<p align="right">(<a href="#readme-top">back to top</a>)</p>

[contributors-shield]: https://img.shields.io/github/contributors/keton/chihuahua.svg?style=for-the-badge
[contributors-url]: https://github.com/keton/chihuahua/graphs/contributors
[forks-shield]: https://img.shields.io/github/forks/keton/chihuahua.svg?style=for-the-badge
[forks-url]: https://github.com/keton/chihuahua/network/members
[stars-shield]: https://img.shields.io/github/stars/keton/chihuahua.svg?style=for-the-badge
[stars-url]: https://github.com/keton/chihuahua/stargazers
[issues-shield]: https://img.shields.io/github/issues/keton/chihuahua.svg?style=for-the-badge
[issues-url]: https://github.com/keton/chihuahua/issues
[license-shield]: https://img.shields.io/github/license/keton/chihuahua.svg?style=for-the-badge
[license-url]: https://github.com/keton/chihuahua/blob/master/LICENSE.txt
[dotnet-badge]: https://img.shields.io/badge/.NET%208.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white
[c-sharp-badge]: https://img.shields.io/badge/C%23-512BD4?style=for-the-badge&logo=csharp&logoColor=white
