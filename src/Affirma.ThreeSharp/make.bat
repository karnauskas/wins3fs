csc /nologo /out:Affirma.ThreeSharp.dll /t:library Model\ObjectCopyRequest.cs Model\ObjectCopyResponse.cs ThreeSharp.cs ThreeSharpConfig.cs ThreeSharpException.cs ThreeSharpUtils.cs Model\ACLGetRequest.cs Model\ACLGetResponse.cs Model\BucketAddRequest.cs Model\BucketAddResponse.cs Model\BucketDeleteRequest.cs Model\BucketDeleteResponse.cs Model\BucketListRequest.cs Model\BucketListResponse.cs Model\ObjectAddRequest.cs Model\ObjectAddResponse.cs Model\ObjectDeleteRequest.cs Model\ObjectDeleteResponse.cs Model\ObjectGetRequest.cs Model\ObjectGetRangeRequest.cs Model\ObjectGetResponse.cs Model\Request.cs Model\Response.cs Model\UrlGetRequest.cs Model\UrlGetResponse.cs Properties\AssemblyInfo.cs Query\ThreeSharpQuery.cs Statistics\ThreeSharpStatistics.cs Model\Transfer.cs

csc /out:Affirma.ThreeSharp.Wrapper.dll /t:library ThreeSharpWrapper.cs Properties\AssemblyInfoWrapper.cs /r:Affirma.ThreeSharp.dll

copy Affirma.ThreeSharp.Wrapper.dll ..
copy Affirma.ThreeSharp.dll ..
