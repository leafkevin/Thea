# 获取当前目录路径
cd $PSScriptRoot
$filelist=dir *.nupkg

foreach($file in $filelist) {
    dotnet nuget push $file -k 123456 -s http://10.0.0.23:8085/v3/index.json
}
pause
