csc /nologo /out:wins3fs-config.exe /target:winexe ConfigThree.cs httpget.cs MessageBoxEx.cs dpapi.cs /r:Affirma.ThreeSharp.dll /r:Affirma.ThreeSharp.Wrapper.dll /r:Microsoft.VisualBasic.dll
csc /nologo /out:awskey_for_config.exe /target:exe awskey_for_config.cs dpapi.cs
