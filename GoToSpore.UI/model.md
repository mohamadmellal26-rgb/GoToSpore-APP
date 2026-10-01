adb devices
dotnet build -t:Run -f net8.0-android

git add . && git commit -m "Fix API user profile endpoints and DTO alignment" && git remote set-url origin https://github.com/mohamadmellal26-rgb/GoToSpore-APP.git && git branch -M main && git push -u origin main