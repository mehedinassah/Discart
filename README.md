\# Discart



\*\*Discart\*\* is a safe, native Windows app that tells you exactly what is taking up your disk space, explains \*why\* it's there, and deletes only what you explicitly approve. 



\## 💡 Why we built this (What it is)

If you've ever looked at your C: drive and wondered, \*"How is my disk full? I haven't downloaded anything!"\* — you are exactly who we built this for.



Most existing tools (like WinDirStat or WizTree) hand you a confusing, color-coded treemap of thousands of files and leave you to Google what `userdata\_20gtxejc.opt\\avd\\userdata.img` actually means. 



Discart doesn't do that. It acts like a helpful mechanic who opens the hood of your car and says, \*"Hey, this specific part is a temporary cache, it's totally safe to throw away. But this other part? Don't touch it, it's your engine."\* 



\## 🛡️ What we have done (The Current State)

We have built a fully functional v1.0 desktop application from the ground up. 



\- \*\*The Verdict System:\*\* Every file or folder is classified into one of five categories:

&#x20; - ✅ \*\*Safe to delete\*\* (Green): Pure caches and temp files.

&#x20; - 🟡 \*\*Probably safe\*\* (Yellow): Rebuildable files, but bigger consequences if wrong.

&#x20; - 🔵 \*\*Needs your call\*\* (Blue): Personal data, games, or unknown folders.

&#x20; - 🟠 \*\*Suspicious\*\* (Orange): Abnormal sizes or known bug patterns.

&#x20; - 🔴 \*\*Don't touch\*\* (Red): System-critical files (locked by the UI).

\- \*\*The "Explain It" Panel:\*\* Click any item, and a bottom panel tells you exactly what it is in plain English (e.g., \*"This is a Google Play Games emulator image. Known bug in the beta: it grows and never shrinks."\*).

\- \*\*The Safety Contract:\*\* The app \*\*never\*\* permanently deletes anything. Every single action goes to the Windows Recycle Bin, and requires a Yes/No confirmation dialog. System files are hard-coded to be unselectable.

\- \*\*Async Scanning:\*\* The UI doesn't freeze. A loading bar animates while the background thread safely calculates folder sizes.



\## 🚀 What we will do in the future (The Roadmap)

\- \*\*v1.1:\*\* A visual Donut Chart on the dashboard so you can see your disk usage at a glance.

\- \*\*v1.2:\*\* "Skip Locked Files" logic. Currently, if a temp file is in use by Windows, the app throws a standard Windows error. We will update it to silently skip locked files and keep going.

\- \*\*v1.3:\*\* Custom App Icons and a modern Windows 11 Fluent Design UI.

\- \*\*v2.0:\*\* An "Update Rules" button. When you open the app, it will ping GitHub to download the latest `rules.json` (so if a new game or app releases a massive 100GB cache, Discart learns about it instantly, like an Antivirus definition file).



\## 🎯 What is the ultimate goal?

Our goal is to make \*\*"Just run Discart"\*\* the standard answer whenever a Windows user asks, \*"Why is my PC so slow/full?"\* 



We want to build the most trusted, transparent, and safest disk utility on the internet. No dark patterns, no hidden telemetry, no auto-deleting your files. Just pure, open-source clarity.



\## 🛠️ How to use it (For Users)

1\. Go to the \[Releases page](https://github.com/mehedinassah/Discart/releases/latest).

2\. Download `Discart-v1.0.0.zip`.

3\. Extract it anywhere (it is 100% portable, no installation required).

4\. Double-click `Discart.exe`. Windows will ask for Administrator permission (this is required so Discart can accurately read the size of protected folders like `C:\\Windows`).

5\. Review the list. Green items are pre-checked. Blue and Red items require your manual inspection.

6\. Click \*\*Delete Selected to Recycle Bin\*\*.



\*Note: Discart is currently an unsigned executable. Windows SmartScreen may warn you on first launch. Click "More info" -> "Run anyway." This is normal for new open-source apps.\*



\## 🤝 How to use it (For Developers)

The "brain" of Discart is the `rules.json` file. If you find an app that hoards disk space in a specific folder, you can add a new rule to the JSON file and submit a Pull Request:



```json

{

&#x20; "id": "my-custom-app",

&#x20; "category": "App data",

&#x20; "paths": \["%LOCALAPPDATA%\\\\MyApp\\\\temp"],

&#x20; "type": "Folder",

&#x20; "verdict": "Safe",

&#x20; "reason": "Temporary files for MyApp. Rebuilds on next launch.",

&#x20; "article": "More detailed explanation here..."

}

