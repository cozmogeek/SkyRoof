# Control the Rotator with the S.A.T. or PstRotator

SkyRoof moves the antenna by sending azimuth and elevation to a server that speaks the **rotctld**
protocol over TCP. In the usual arrangement, described in
[Setting Up Rotator Control](setting_up_rotator_control.md), that server is **rotctld.exe** from
HamLib. Two popular alternatives speak the same protocol themselves, so rotctld.exe is not needed at
all:

- the **S.A.T.** satellite tracker from CSN Technologies, which appears on your network as a Hamlib
  rotator server;
- **PstRotator** by YO3DMU, which has a built-in rotctld server.

In both cases SkyRoof predicts the pass and computes the bearings, and the device or the program
moves the rotator. This section covers rotator control only — the radio is still tuned by SkyRoof as
described in [Setting Up CAT Control](setting_up_cat_control.md).

## Which Option to Use

From inside SkyRoof the three arrangements are identical: the same three settings, the same
[Rotator Control](rotator_control.md) panel, the same rotation algorithm, and the same manual
pointing, parking and stop commands. They differ in what is wired to the rotator controller, and in
what you have to start and keep running:

| | rotctld.exe | S.A.T. | PstRotator |
|---|---|---|---|
| **Wired to the rotator controller** | a COM port of the computer that runs SkyRoof | the S.A.T., over its own cable | a COM port of the computer that runs PstRotator |
| **Controllers supported** | the [HamLib list](https://github.com/Hamlib/Hamlib/wiki/Supported-Rotators) | Yaesu G-5400 / G-5500 / G-5600 and PstRotator, and SPID in the recent firmware | the [PstRotator list](http://www.qsl.net/yo3dmu/index_Page346.htm) |
| **To start before a session** | the rotctld.exe console window | nothing, the S.A.T. always listens | the PstRotator window |
| **Pointing the antenna when SkyRoof is not running** | not possible, rotctld.exe has no user interface | from the S.A.T. web page or its front panel | from the PstRotator window |
| **Cost** | free | the price of the device | the price of the license |

**Use rotctld.exe** when the rotator controller is cabled to the computer that runs SkyRoof and
HamLib supports it. This is the shortest path from SkyRoof to the rotator — one program, one
shortcut, nothing to buy — and it is the arrangement described in
[Setting Up Rotator Control](setting_up_rotator_control.md).

**Use the S.A.T.** when you already own one, or when the rotator controller is not near the computer:
the S.A.T. sits at the rotator and is reached over the network, and it also tracks satellites and
parks the antenna on its own when SkyRoof is not running. Its built-in tracker must stay idle while
SkyRoof is tracking.

**Use PstRotator** when you already run it for your other antennas, when your rotator controller is
supported by PstRotator but not by HamLib, or when you want its maps and its manual controls at hand
during a pass. It is the only one of the three that costs a license fee if you do not already use it.

Each of these puts one program or device between SkyRoof and the rotator. Longer chains are possible
— the S.A.T. can drive PstRotator, and rotctld.exe can drive PstRotator through its UDP interface —
but every added link is one more place where the bearings can stop flowing, so build one only when
the rotator controller cannot be reached any other way.

## Using the S.A.T.

The S.A.T. listens for rotator commands on TCP port **4533**, and this server is enabled by default,
so no setting has to be changed to turn it on.

1. In the S.A.T. web interface open the **ROTATOR** panel, set **TYPE** to your rotator model, set the
   travel limits and the park and ready positions as usual, and click **SAVE**.
2. Open the **NETWORK** panel and note the IP address of the S.A.T. It is also shown on the S.A.T.
   display. The **PORTS** fields on the same panel show which ports the S.A.T. listens on; leave the
   rotator port at 4533 unless it conflicts with something else on your network.
3. Make sure the antenna is enabled in the S.A.T. — the **ANT ENABLE** button flashes red when it is
   not.
4. **Do not start tracking on the S.A.T. itself.** SkyRoof and the built-in tracker of the S.A.T.
   would then send conflicting bearings to the same rotator. Let the S.A.T. sit idle and leave the
   tracking to SkyRoof.
5. In SkyRoof, click **Tools / Settings** and set, in the **Rotator** section:

    - **Host** - the IP address of the S.A.T.;
    - **TCP Port** - 4533;
    - **Enabled** - True.

SkyRoof uses only three rotctld commands - set the position, read the position and stop - and these
are exactly the commands that the S.A.T. implements. Other tracking programs may require a more
complete Hamlib implementation and have to reach the S.A.T. through a HamLib backend; SkyRoof
connects to it directly.

Set **Minimum Azimuth**, **Maximum Azimuth**, **Minimum Elevation** and **Maximum Elevation** in the
SkyRoof settings to the same travel limits that you configured in the S.A.T., so that SkyRoof does
not ask for a bearing that the rotator cannot reach.

## Using PstRotator

Before you connect SkyRoof, configure PstRotator for your rotator controller and check in the
**Manual** mode that it moves the antenna. SkyRoof only supplies the bearings; everything about the
controller itself stays in PstRotator.

PstRotator listens on TCP port **4533** and SkyRoof connects to it directly. Its rotctld server has
been part of the program since version 14.93.

1. In PstRotator, open the **Setup** menu and check **Rotctld Hamlib Server**. The option is off in a
   new installation, and the program starts listening on port 4533 as soon as you check it.
2. Open **Communication / Rotctld Server Setup...**, and in the **Rotctld Setup** window check that
   **Port number** is **4533** and that **Extended protocol** is **unchecked**, which is how a new
   installation comes. SkyRoof expects the plain replies of the standard protocol. Click **Save
   Settings** to close the window.
3. Leave **Mode** set to **Manual** in the main window of PstRotator. The **Manual / Tracking**
   selector belongs to the trackers of PstRotator itself: with **Tracking** selected, the program
   follows whatever is chosen in its **Tracker** menu — the Moon in a new installation — and
   overrides the bearings that SkyRoof sends. The rotctld server accepts positions in either mode.
4. If your rotator can point higher than 90° in elevation, check **180 deg Elevation** in the
   **Setup** menu, so that PstRotator accepts the flipped bearings that SkyRoof sends on high passes.
   Without this option PstRotator answers a flipped bearing with "RPRT 0" but silently moves to 90°
   instead of the elevation it was given.
5. In SkyRoof, click **Tools / Settings** and set, in the **Rotator** section:

    - **Host** - "127.0.0.1" if PstRotator runs on the same computer as SkyRoof, or the address of
        the computer where it runs;
    - **TCP Port** - 4533;
    - **Enabled** - True.

The server of PstRotator accepts the set position, read position, stop and park commands, and
SkyRoof uses the first three of them, so everything on the
[Rotator Control](rotator_control.md) panel works, including the **Stop** button.

Do not run rotctld.exe at the same time. Both programs would try to listen on port 4533 and only one
of them would get it.

## Checking the Connection

Click on the **Rotator** label on the status bar to enable and disable rotator control, and watch the
[Rotator Control](rotator_control.md) panel: the small numbers below the satellite position are the
antenna bearing as reported by the S.A.T. or by PstRotator. If they follow the satellite, the
connection works. A pink color on the panel means that the antenna is not pointing where SkyRoof
asked it to.

If nothing moves, set **Log Traffic** to True in the **Rotator** settings, reproduce the problem, and
look at the log file in the [data folder](data_folder.md) to see the commands and the replies.

## See Also

- [Setting Up Rotator Control](setting_up_rotator_control.md)
- [Rotator Control](rotator_control.md)
- [Smart Antenna Rotation](smart_antenna_rotation.md)
