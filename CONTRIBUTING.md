# 开发与维护

项目使用 Windows 系统 C# 编译器、WPF 和 Windows PowerShell 5.1。构建方法见 README。

修改发现逻辑时，先添加隔离材料并检查不同证据是否混淆；修改界面时，保留卡片到独立详情页的交互、唯一目录管理入口和“本地位置”标签。更新版本时同步 VERSION、程序集、manifest、界面、说明和验证记录。

验证命令：

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Run-Tests.ps1
~~~

不得提交真实扫描清单、用户目录配置、密钥、Data、TestData 或个人日志。测试应由脚本生成隔离文件，不依赖维护者的本机工具。功能文档和工具文本均为数据，不可作为程序执行指令。

项目当前未选定开放源代码许可证；提交或分发安排由仓库所有者决定。
