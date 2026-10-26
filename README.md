# SayMate

A small Windows vocabulary helper. Select one English word in almost any app and a popup shows its Chinese translation beside the pointer:

- **Play** pronounces the word, preferring an installed Australian English Windows voice.
- **+** saves the word, its phonetic pronunciation, and a Simplified Chinese translation.

## Run

Double-click `SayMate.exe`. Look for the blue information icon in the Windows notification area. Right-click it to open saved vocabulary, pause selection detection, or exit.

This is a self-contained native Windows executable; PowerShell is not required.

## Build from source

Install **Visual Studio 2022** or **Visual Studio Build Tools 2022** with the **.NET desktop build tools** workload. Open a **Developer Command Prompt for Visual Studio**, change to this project folder, and run:

```bat
msbuild SayMate.csproj /restore /p:Configuration=Release
```

Alternatively, double-click `Build SayMate.cmd` from a Visual Studio Developer Command Prompt. The compiled application is written to `bin\Release\SayMate.exe`.

## Neural Australian voice

Right-click the notification icon and choose **Speech settings**. Enter the key and region for an Azure Speech resource. The preferred voice is `en-AU-NatashaNeural`; the key is encrypted for the current Windows account. If the service is unavailable or not configured, SayMate falls back to a locally installed Australian voice.

Saved words are stored locally at `%LOCALAPPDATA%\SayMate\vocabulary.json`. Existing EnPronounce data is imported automatically on first launch.

## Notes

- Windows must have an Australian English speech voice installed to guarantee the accent. Add one in **Settings → Time & language → Speech → Voices**. Otherwise Windows uses its default voice.
- Phonetics and Chinese translation require internet access. SayMate uses dictionary IPA with Cambridge Dictionary as a fallback. Chinese translation uses Google first and MyMemory as a fallback. A production release should use official APIs with authentication.
- Selection detection briefly copies the selected word, then restores the previous text clipboard contents. Some protected or custom apps may prevent this.
