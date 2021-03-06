﻿using System;
using System.IO;
using System.Linq;
using Nancy;
using Nancy.ModelBinding;
using Newtonsoft.Json;
using PoGoMITM.Base.Cache;
using PoGoMITM.Base.Config;
using PoGoMITM.Base.Dumpers;
using PoGoMITM.Base.Models;
using PoGoMITM.Base.Utils;
using PoGoMITM.Launcher.Models;
using POGOProtos.Networking.Envelopes;
using PoGoMITM.Base.ProtoHelpers;

namespace PoGoMITM.Launcher.Modules
{
    public class HomeModule : NancyModule
    {
        public HomeModule()
        {
            Get["/"] = x => View["Index", RequestContextListModel.Instance];

            Get["/details/{guid}"] = x =>
            {
                var guid = (string)x.guid;
                var context = GetRequestContext(guid);
                return Response.AsText(JsonConvert.SerializeObject(context), "text/json");
            };

            Get["/download/cert"] = x =>
            {
                var cert = CertificateHelper.GetCertificateFromStore();
                var pem = CertificateHelper.ConvertToPem(cert);

                return Response.AsText(pem).AsAttachment($"{AppConfig.RootCertificateName}.cer", "application/x-x509-ca-cert");
            };

            Get["/download/request/raw/{guid}"] = x =>
            {
                var guid = (string)x.guid;
                var context = GetRequestContext(guid);
                if (context?.RequestData.RequestBody != null)
                {
                    return Response.FromStream(new MemoryStream(context.RequestData.RequestBody), "application/binary").AsAttachment(guid + "-request.bin");
                }
                return new NotFoundResponse();
            };

            Get["/download/request/decoded/{guid}", true] = async (x, ct) =>
            {
                var guid = (string)x.guid;
                var context = GetRequestContext(guid);
                if (context == null) return new NotFoundResponse();
                if (context.RequestData.RawDecodedRequestBody == null)
                {
                    context.RequestData.RawDecodedRequestBody = await Protoc.DecodeRaw(context.RequestData.RequestBody);
                }
                return Response.AsText(context.RequestData.RawDecodedRequestBody).AsAttachment(guid + "-request.txt", "text/plain");
            };


            Get["/download/response/raw/{guid}"] = x =>
            {
                var guid = (string)x.guid;
                var context = GetRequestContext(guid);
                if (context?.ResponseData.ResponseBody != null)
                {
                    return Response.FromStream(new MemoryStream(context.ResponseData.ResponseBody), "application/binary").AsAttachment(guid + "-response.bin");
                }
                return new NotFoundResponse();
            };

            Get["/download/rawsignature/{guid}"] = x =>
            {
                var guid = (string)x.guid;
                var context = GetRequestContext(guid);
                if (context?.RequestData.RequestBody != null)
                {
                    return Response.FromStream(new MemoryStream(context.RequestData.RawEncryptedSignature), "application/binary").AsAttachment(guid + "-rawsignature.bin");
                }
                return new NotFoundResponse();
            };


            Get["/download/decryptedrawsignature/{guid}"] = x =>
            {
                var guid = (string)x.guid;
                var context = GetRequestContext(guid);
                if (context?.RequestData.RequestBody != null)
                {
                    return Response.FromStream(new MemoryStream(context.RequestData.RawDecryptedSignature), "application/binary").AsAttachment(guid + "-decryptedsignature.bin");
                }
                return new NotFoundResponse();
            };

            Get["/download/response/decoded/{guid}", true] = async (x, ct) =>
            {
                var guid = (string)x.guid;
                var context = GetRequestContext(guid);
                if (context == null) return new NotFoundResponse();
                if (context.ResponseData.RawDecodedResponseBody == null)
                {
                    context.ResponseData.RawDecodedResponseBody = await Protoc.DecodeRaw(context.ResponseData.ResponseBody);
                }

                return Response.AsText(context.ResponseData.RawDecodedResponseBody).AsAttachment(guid + "-response.txt", "text/plain");

            };

            Get["/download/json/{guid}"] = x =>
            {
                var guid = (string)x.guid;
                var context = GetRequestContext(guid);
                if (context != null)
                {
                    return Response.AsText(JsonConvert.SerializeObject(context, Formatting.Indented)).AsAttachment(guid + ".json", "application/json");
                }
                return new NotFoundResponse();
            };

            Get["/session/{session}"] = x =>
            {
                var fileName = (string)x.session;
                if (fileName == "live")
                {
                    var liveContext =
                        ContextCache.RawContexts.Values.Where(r => r.IsLive)
                            .OrderBy(c => c.RequestTime).Select(RequestContextListModel.FromRawContext);
                    return Response.AsText(JsonConvert.SerializeObject(liveContext), "text/json");
                }
                var sessionDump = RawDumpReader.GetSession(fileName);
                if (sessionDump != null)
                {
                    foreach (var rawContext in sessionDump.Where(r => r != null))
                    {
                        ContextCache.RawContexts.TryAdd(rawContext.Guid, rawContext);
                    }
                    var list = sessionDump.Where(r => r != null).Select(RequestContextListModel.FromRawContext).ToList();
                    return Response.AsText(JsonConvert.SerializeObject(list), "text/json");
                }
                return new NotFoundResponse();
            };

            Post["/details/signature/{guid}"] = x =>
            {
                try
                {
                    var guid = (string)x.guid;
                    var context = GetRequestContext(guid);

                    var post = this.Bind<DecryptedSignature>();
                    var trimmed = post.Bytes.Substring(1);
                    trimmed = trimmed.Substring(0, trimmed.Length - 1);
                    var res = trimmed.Split(',');
                    var arr = new byte[res.Length];
                    arr = res.Select(byte.Parse).ToArray();
                    context.RequestData.RawDecryptedSignature = arr;

                    var signature = Signature.Parser.ParseFrom(arr);
                    context.RequestData.DecryptedSignature = signature;
                    return Response.AsText(JsonConvert.SerializeObject(new { success = true, signature = signature }), "text/json");
                }
                catch (Exception ex)
                {
                    return Response.AsText(JsonConvert.SerializeObject(new { success = false, exception = ex }), "text/json");
                }
            };
        }

        private static RequestContext GetRequestContext(string guid)
        {
            var parsedGuid = Guid.Parse(guid);

            RawContext rawContext;
            if (ContextCache.RawContexts.TryGetValue(parsedGuid, out rawContext))
            {
                return RequestContext.GetInstance(rawContext);
            }
            return null;
        }

    }

    public class DecryptedSignature
    {
        public string Bytes { get; set; }
    }
}
