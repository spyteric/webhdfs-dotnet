﻿using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using System.Web;

namespace WebHDFS
{
    /// <summary>
    /// WebHDFS Client.
    /// </summary>
    public class WebHDFSClient : IDisposable
    {
        readonly string _baseAPI;
        readonly HttpClient _httpClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="T:WebHDFS.WebHDFSClient"/> class.
        /// </summary>
        /// <param name="BaseAPI">Base API.</param>
        /// <param name="Credentials">Optional credentials.</param>
        /// <param name="Timeout">Optional timeout.</param>
        /// <param name="CustomHttpMessageHandler">Optional HttpMessageHandler.</param>
        /// <param name="ExpectContinue">Expect server will return continue (100) before success</param>
        public WebHDFSClient(string BaseAPI, NetworkCredential Credentials = null, TimeSpan? Timeout = null, HttpMessageHandler CustomHttpMessageHandler = null, bool? ExpectContinue = null)
        {
            _baseAPI = BaseAPI.TrimEnd('/');

            HttpMessageHandler httpMessageHandler;
            if (CustomHttpMessageHandler != null)
            {
                httpMessageHandler = CustomHttpMessageHandler;
            }
            else
            {
                httpMessageHandler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    Credentials = Credentials,
                    PreAuthenticate = true
                    
                };
            }

            _httpClient = new HttpClient(httpMessageHandler)
            {
                Timeout = Timeout.GetValueOrDefault(TimeSpan.FromMinutes(5))
            };
            _httpClient.DefaultRequestHeaders.ExpectContinue = ExpectContinue.GetValueOrDefault();
        }

        /// <summary>
        /// https://hadoop.apache.org/docs/r2.8.0/hadoop-project-dist/hadoop-hdfs/WebHDFS.html#Create_and_Write_to_a_File
        /// </summary>
        public async Task<bool> UploadFile(
            string filePath,
            string path,
            bool overwrite = false,
            long? blocksize = null,
            short? replication = null,
            string permission = null,
            int? buffersize = null)
        {
            using (var fileStream = new FileStream(filePath, FileMode.Open))
            {
                return await WriteStream(fileStream, path, overwrite, blocksize, replication, permission, buffersize);
            }
        }

        /// <summary>
        /// https://hadoop.apache.org/docs/r2.8.0/hadoop-project-dist/hadoop-hdfs/WebHDFS.html#Create_and_Write_to_a_File
        /// Note: You are responsible for closing the stream you send in.
        /// </summary>
        public async Task<bool> WriteStream(
            Stream stream,
            string path,
            bool overwrite = false,
            long? blocksize = null,
            short? replication = null,
            string permission = null,
            int? buffersize = null)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            WebHDFSHttpQueryParameter.SetOp(query, WebHDFSHttpQueryParameter.Op.CREATE);
            WebHDFSHttpQueryParameter.SetOverwrite(query, overwrite);
            WebHDFSHttpQueryParameter.setBlocksize(query, blocksize);
            WebHDFSHttpQueryParameter.SetReplication(query, replication);
            WebHDFSHttpQueryParameter.SetPermission(query, permission);
            WebHDFSHttpQueryParameter.SetBuffersize(query, buffersize);

            string requestPath = _baseAPI + path + '?' + query;
            
            var response = await _httpClient.PutAsync(requestPath, new ByteArrayContent(new byte[] {}));
            if(response.StatusCode.Equals(HttpStatusCode.TemporaryRedirect))
            {
                
                var response2 = await _httpClient.PutAsync(response.Headers.Location, new StreamContent(stream));
                try
                {
                    response2.EnsureSuccessStatusCode();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    throw;
                }
                
                return response2.IsSuccessStatusCode;
            }
            throw new InvalidOperationException("Should get a 307. Instead we got: " + 
                                                response.StatusCode + " " + response.ReasonPhrase);
        }

        /// <summary>
        /// https://hadoop.apache.org/docs/r2.8.0/hadoop-project-dist/hadoop-hdfs/WebHDFS.html#Append_to_a_File
        /// </summary>
        public async Task<bool> AppendFile(
            string filePath,
            string path,
            int? buffersize = null)
        {
            using (var fileStream = new FileStream(filePath, FileMode.Open))
            {
                return await AppendStream(fileStream, path, buffersize);
            }
        }

        /// <summary>
        /// https://hadoop.apache.org/docs/r2.8.0/hadoop-project-dist/hadoop-hdfs/WebHDFS.html#Append_to_a_File
        /// Note: You are responsible for closing the stream you send in.
        /// </summary>
        public async Task<bool> AppendStream(
            Stream stream,
            string path,
            int? buffersize = null)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            WebHDFSHttpQueryParameter.SetOp(query, WebHDFSHttpQueryParameter.Op.APPEND);
            WebHDFSHttpQueryParameter.SetBuffersize(query, buffersize);

            string requestPath = _baseAPI + path + '?' + query;

            var response = await _httpClient.PostAsync(requestPath, new ByteArrayContent(new byte[] { }));
            if (response.StatusCode.Equals(HttpStatusCode.TemporaryRedirect))
            {
                var response2 = await _httpClient.PostAsync(response.Headers.Location, new StreamContent(stream));
                response2.EnsureSuccessStatusCode();
                return response2.IsSuccessStatusCode;
            }
            throw new InvalidOperationException("Should get a 307. Instead we got: " +
                                                response.StatusCode + " " + response.ReasonPhrase);
        }

        /// <summary>
        /// https://hadoop.apache.org/docs/r2.8.0/hadoop-project-dist/hadoop-hdfs/WebHDFS.html#Open_and_Read_a_File
        /// </summary>
        public async Task<bool> DownloadFile(
            string filePath,
            string path,
            long? offset = null,
            long? length = null,
            int? buffersize = null)
        {
            string pathname = Path.GetFullPath(filePath);
            if (File.Exists(pathname))
            {
                throw new InvalidOperationException(string.Format("File {0} already exists.", pathname));
            }

            using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                return await ReadStream(fileStream, path, offset, length, buffersize);
            }
        }

        /// <summary>
        /// https://hadoop.apache.org/docs/r2.8.0/hadoop-project-dist/hadoop-hdfs/WebHDFS.html#Open_and_Read_a_File
        /// Note: You are responsible for closing the stream you send in.
        /// </summary>
        public async Task<bool> ReadStream(
            Stream stream,
            string path,
            long? offset = null,
            long? length = null,
            int? buffersize = null)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            WebHDFSHttpQueryParameter.SetOp(query, WebHDFSHttpQueryParameter.Op.OPEN);
            WebHDFSHttpQueryParameter.SetOffset(query, offset);
            WebHDFSHttpQueryParameter.SetLength(query, length);
            WebHDFSHttpQueryParameter.SetBuffersize(query, buffersize);

            string requestPath = _baseAPI + path + '?' + query;

            var response = await _httpClient.GetAsync(requestPath);
            if (response.StatusCode.Equals(HttpStatusCode.TemporaryRedirect))
            {
                var response2 = await _httpClient.GetAsync(response.Headers.Location);
                response2.EnsureSuccessStatusCode();
                if (response2.IsSuccessStatusCode)
                {
                    await response2.Content.CopyToAsync(stream);
                }
                return response2.IsSuccessStatusCode;
            }
            throw new InvalidOperationException("Should get a 307. Instead we got: " +
                                                response.StatusCode + " " + response.ReasonPhrase);
        }

        /// <summary>
        /// https://hadoop.apache.org/docs/r2.8.0/hadoop-project-dist/hadoop-hdfs/WebHDFS.html#Make_a_Directory
        /// </summary>
        public async Task<Boolean> MakeDirectory(
            string path,
            string permission = null)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            WebHDFSHttpQueryParameter.SetOp(query, WebHDFSHttpQueryParameter.Op.MKDIRS);
            WebHDFSHttpQueryParameter.SetPermission(query, permission);

            string requestPath = _baseAPI + path + '?' + query;

            var response = await _httpClient.PutAsync(requestPath, new ByteArrayContent(new byte[] { }));
            response.EnsureSuccessStatusCode();
            var serializer = new DataContractJsonSerializer(typeof(Boolean));
            return ((Boolean)serializer.ReadObject(await response.Content.ReadAsStreamAsync()));
        }

        /// <summary>
        /// https://hadoop.apache.org/docs/r2.8.0/hadoop-project-dist/hadoop-hdfs/WebHDFS.html#Status_of_a_FileDirectory
        /// </summary>
        public async Task<FileStatus> GetFileStatus(
            string path)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            WebHDFSHttpQueryParameter.SetOp(query, WebHDFSHttpQueryParameter.Op.GETFILESTATUS);

            string requestPath = _baseAPI + path + '?' + query;

            var response = await _httpClient.GetAsync(requestPath);
            response.EnsureSuccessStatusCode();
            var serializer = new DataContractJsonSerializer(typeof(FileStatusClass));
            return ((FileStatusClass)serializer.ReadObject(await response.Content.ReadAsStreamAsync())).FileStatus;
        }

        /// <summary>
        /// https://hadoop.apache.org/docs/r2.8.0/hadoop-project-dist/hadoop-hdfs/WebHDFS.html#List_a_Directory
        /// </summary>
        public async Task<FileStatuses> ListStatus(
            string path)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            WebHDFSHttpQueryParameter.SetOp(query, WebHDFSHttpQueryParameter.Op.LISTSTATUS);

            string requestPath = _baseAPI + path + '?' + query;

            var response = await _httpClient.GetAsync(requestPath);
            response.EnsureSuccessStatusCode();
            var serializer = new DataContractJsonSerializer(typeof(FileStatusesClass));
            return ((FileStatusesClass)serializer.ReadObject(await response.Content.ReadAsStreamAsync())).FileStatuses;
        }

        /// <summary>
        /// https://hadoop.apache.org/docs/r2.8.0/hadoop-project-dist/hadoop-hdfs/WebHDFS.html#Get_Content_Summary_of_a_Directory
        /// </summary>
        public async Task<ContentSummary> GetContentSummary(
            string path)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            WebHDFSHttpQueryParameter.SetOp(query, WebHDFSHttpQueryParameter.Op.GETCONTENTSUMMARY);

            string requestPath = _baseAPI + path + '?' + query;

            var response = await _httpClient.GetAsync(requestPath);
            response.EnsureSuccessStatusCode();
            var serializer = new DataContractJsonSerializer(typeof(ContentSummaryClass));
            return ((ContentSummaryClass)serializer.ReadObject(await response.Content.ReadAsStreamAsync())).ContentSummary;
        }

        /// <summary>
        /// https://hadoop.apache.org/docs/r2.8.0/hadoop-project-dist/hadoop-hdfs/WebHDFS.html#Get_File_Checksum
        /// </summary>
        public async Task<FileChecksum> GetFileChecksum(
            string path)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            WebHDFSHttpQueryParameter.SetOp(query, WebHDFSHttpQueryParameter.Op.GETFILECHECKSUM);

            string requestPath = _baseAPI + path + '?' + query;

            var response = await _httpClient.GetAsync(requestPath);
            if (response.StatusCode.Equals(HttpStatusCode.TemporaryRedirect))
            {
                var response2 = await _httpClient.GetAsync(response.Headers.Location);
                response2.EnsureSuccessStatusCode();
                var serializer = new DataContractJsonSerializer(typeof(FileChecksumClass));
                return ((FileChecksumClass)serializer.ReadObject(await response2.Content.ReadAsStreamAsync())).FileChecksum;
            }
            throw new InvalidOperationException("Should get a 307. Instead we got: " +
                                                response.StatusCode + " " + response.ReasonPhrase);
        }

        /// <summary>
        /// https://hadoop.apache.org/docs/r2.8.0/hadoop-project-dist/hadoop-hdfs/WebHDFS.html#Rename_a_FileDirectory
        /// </summary>
        public async Task<Boolean> Rename(
            string path,
            string destination)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            WebHDFSHttpQueryParameter.SetOp(query, WebHDFSHttpQueryParameter.Op.RENAME);
            WebHDFSHttpQueryParameter.SetDestination(query, destination);

            string requestPath = _baseAPI + path + '?' + query;

            var response = await _httpClient.PutAsync(requestPath, new ByteArrayContent(new byte[] { }));
            response.EnsureSuccessStatusCode();
            var serializer = new DataContractJsonSerializer(typeof(Boolean));
            return ((Boolean)serializer.ReadObject(await response.Content.ReadAsStreamAsync()));
        }

        /// <summary>
        /// https://hadoop.apache.org/docs/r2.8.0/hadoop-project-dist/hadoop-hdfs/WebHDFS.html#Delete_a_FileDirectory
        /// </summary>
        public async Task<Boolean> Delete(
            string path,
            bool? recursive = null)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            WebHDFSHttpQueryParameter.SetOp(query, WebHDFSHttpQueryParameter.Op.DELETE);
            WebHDFSHttpQueryParameter.SetRecursive(query, recursive);

            string requestPath = _baseAPI + path + '?' + query;

            var response = await _httpClient.DeleteAsync(requestPath);
            response.EnsureSuccessStatusCode();
            var serializer = new DataContractJsonSerializer(typeof(Boolean));
            return ((Boolean)serializer.ReadObject(await response.Content.ReadAsStreamAsync()));
        }

        /// <summary>
        /// https://hadoop.apache.org/docs/r2.8.0/hadoop-project-dist/hadoop-hdfs/WebHDFS.html#Truncate_a_File
        /// </summary>
        public async Task<Boolean> Truncate(
            string path,
            long newlength)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            WebHDFSHttpQueryParameter.SetOp(query, WebHDFSHttpQueryParameter.Op.TRUNCATE);
            WebHDFSHttpQueryParameter.SetNewLength(query, newlength);

            string requestPath = _baseAPI + path + '?' + query;

            var response = await _httpClient.PostAsync(requestPath, new ByteArrayContent(new byte[] { }));
            response.EnsureSuccessStatusCode();
            var serializer = new DataContractJsonSerializer(typeof(Boolean));
            return ((Boolean)serializer.ReadObject(await response.Content.ReadAsStreamAsync()));
        }

        /// <summary>
        /// https://hadoop.apache.org/docs/r2.8.0/hadoop-project-dist/hadoop-hdfs/WebHDFS.html#Concat_Files
        /// </summary>
        public async Task<bool> Concat(
            string path,
            string sources)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            WebHDFSHttpQueryParameter.SetOp(query, WebHDFSHttpQueryParameter.Op.CONCAT);
            WebHDFSHttpQueryParameter.SetSources(query, sources);

            string requestPath = _baseAPI + path + '?' + query;

            var response = await _httpClient.PostAsync(requestPath, new ByteArrayContent(new byte[] { }));
            response.EnsureSuccessStatusCode();
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// https://hadoop.apache.org/docs/r2.8.0/hadoop-project-dist/hadoop-hdfs/WebHDFS.html#Create_a_Symbolic_Link
        /// </summary>
        public async Task<bool> CreateSymlink(
            string path,
            string destination,
            bool? createParent)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            WebHDFSHttpQueryParameter.SetOp(query, WebHDFSHttpQueryParameter.Op.CREATESYMLINK);
            WebHDFSHttpQueryParameter.SetDestination(query, destination);
            WebHDFSHttpQueryParameter.SetCreateParent(query, createParent);

            string requestPath = _baseAPI + path + '?' + query;

            var response = await _httpClient.PutAsync(requestPath, new ByteArrayContent(new byte[] { }));
            response.EnsureSuccessStatusCode();
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// https://hadoop.apache.org/docs/r2.8.0/hadoop-project-dist/hadoop-hdfs/WebHDFS.html#Status_of_a_FileDirectory
        /// </summary>
        public async Task<string> GetHomeDirectory()
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            WebHDFSHttpQueryParameter.SetOp(query, WebHDFSHttpQueryParameter.Op.GETHOMEDIRECTORY);

            string requestPath = _baseAPI + '?' + query;

            var response = await _httpClient.GetAsync(requestPath);
            response.EnsureSuccessStatusCode();
            var serializer = new DataContractJsonSerializer(typeof(PathClass));
            return ((PathClass)serializer.ReadObject(await response.Content.ReadAsStreamAsync())).Path;
        }

        /// <summary>
        /// https://hadoop.apache.org/docs/r2.8.0/hadoop-project-dist/hadoop-hdfs/WebHDFS.html#Set_Permission
        /// </summary>
        public async Task<bool> SetPermission(
            string path,
            string permission = null)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            WebHDFSHttpQueryParameter.SetOp(query, WebHDFSHttpQueryParameter.Op.SETPERMISSION);
            WebHDFSHttpQueryParameter.SetPermission(query, permission);

            string requestPath = _baseAPI + path + '?' + query;

            var response = await _httpClient.PutAsync(requestPath, new ByteArrayContent(new byte[] { }));
            response.EnsureSuccessStatusCode();
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// https://hadoop.apache.org/docs/r2.8.0/hadoop-project-dist/hadoop-hdfs/WebHDFS.html#Set_Owner
        /// </summary>
        public async Task<bool> SetOwner(
            string path,
            string owner = null,
            string group = null)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            WebHDFSHttpQueryParameter.SetOp(query, WebHDFSHttpQueryParameter.Op.SETOWNER);
            WebHDFSHttpQueryParameter.SetOwner(query, owner);
            WebHDFSHttpQueryParameter.SetGroup(query, group);

            string requestPath = _baseAPI + path + '?' + query;

            var response = await _httpClient.PutAsync(requestPath, new ByteArrayContent(new byte[] { }));
            response.EnsureSuccessStatusCode();
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// https://hadoop.apache.org/docs/r2.8.0/hadoop-project-dist/hadoop-hdfs/WebHDFS.html#Set_Replication_Factor
        /// </summary>
        public async Task<Boolean> SetReplication(
            string path,
            short? replication = null)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            WebHDFSHttpQueryParameter.SetOp(query, WebHDFSHttpQueryParameter.Op.SETREPLICATION);
            WebHDFSHttpQueryParameter.SetReplication(query, replication);

            string requestPath = _baseAPI + path + '?' + query;

            var response = await _httpClient.PutAsync(requestPath, new ByteArrayContent(new byte[] { }));
            response.EnsureSuccessStatusCode();
            var serializer = new DataContractJsonSerializer(typeof(Boolean));
            return ((Boolean)serializer.ReadObject(await response.Content.ReadAsStreamAsync()));
        }

        /// <summary>
        /// https://hadoop.apache.org/docs/r2.8.0/hadoop-project-dist/hadoop-hdfs/WebHDFS.html#Set_Replication_Factor
        /// </summary>
        public async Task<Boolean> SetTimes(
            string path,
            short? modificationtime = null,
            short? accesstime = null)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            WebHDFSHttpQueryParameter.SetOp(query, WebHDFSHttpQueryParameter.Op.SETTIMES);
            WebHDFSHttpQueryParameter.SetModificationTime(query, modificationtime);
            WebHDFSHttpQueryParameter.SetAccessTime(query, accesstime);

            string requestPath = _baseAPI + path + '?' + query;

            var response = await _httpClient.PutAsync(requestPath, new ByteArrayContent(new byte[] { }));
            response.EnsureSuccessStatusCode();
            var serializer = new DataContractJsonSerializer(typeof(Boolean));
            return ((Boolean)serializer.ReadObject(await response.Content.ReadAsStreamAsync()));
        }

        /// <summary>
        /// https://hadoop.apache.org/docs/r2.7.3/hadoop-project-dist/hadoop-hdfs/WebHDFS.html#Check_access
        /// </summary>
        public async Task<bool> CheckAccess(
            string path,
            string fsaction)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            WebHDFSHttpQueryParameter.SetOp(query, WebHDFSHttpQueryParameter.Op.CHECKACCESS);
            WebHDFSHttpQueryParameter.SetFSAction(query, fsaction);

            string requestPath = _baseAPI + path + '?' + query;
            var response = await _httpClient.GetAsync(requestPath);
            response.EnsureSuccessStatusCode();
            return response.IsSuccessStatusCode;
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        /// <summary>
        /// This code added to correctly implement the disposable pattern.
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if(_httpClient != null)
                    {
                        _httpClient.Dispose();
                    }
                }

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~WebHDFSClient() {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        /// <summary>
        /// This code added to correctly implement the disposable pattern.
        /// </summary>
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
}
