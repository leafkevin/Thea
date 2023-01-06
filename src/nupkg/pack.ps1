# 获取当前目录路径
cd $PSScriptRoot

# 定义所有项目
$projects = (
  "Thea.Job"
)
Remove-Item *.nupkg -recurse
cd ..

# 打包
foreach($project in $projects) {    
    cd $project
	dotnet clean
	dotnet build -c Release
    dotnet pack -c Release
    mv ./bin/Release/*.nupkg ../nupkg
    cd ..
}
pause