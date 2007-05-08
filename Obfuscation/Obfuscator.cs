using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Security.Cryptography;

namespace Obfuscation
{
    /// <summary>
    /// This is the class that does all the obfuscation. The obfuscation perfomed is -very-
    /// specific. No HTML is obfuscated at all, which limits the use of HTML defined variables
    /// in your PHP code. Input form values can therefore be accessed using the $_REQUEST array 
    /// varaible, but variables defined by the name of the input fields in HTML will not be available.
    /// </summary>
    public class Obfuscator
    {
        IObfuscatorUI _ui = null;

        bool _removeWhitespace = false;
        bool _obfuscateVariables = false;
        bool _obfuscateFunctionNames = false;
 
        List<string> _targetFiles = null;
        List<string> _excludedFunctions = new List<string>();
        List<string> _excludedVariables = new List<string>();

        List<string> _functionsToReplace = new List<string>();
        List<string> _classesToReplace = new List<string>();

        ObfuscationConfig _config = null;

        MD5CryptoServiceProvider _md5 = new MD5CryptoServiceProvider();

        /// <summary>
        /// Constructs an Obfuscator object
        /// </summary>
        /// <param name="ui">User interface which will recieve status updates as the obfuscator runs.</param>
        public Obfuscator(IObfuscatorUI ui)
        {
            _ui = ui;
        }

        /// <summary>
        /// Sets or Gets the flag indicating whether the obfuscator should rename variables
        /// </summary>
        public bool ObfuscateVariables
        {
            get { return _obfuscateVariables; }
            set { _obfuscateVariables = value; }
        }

        /// <summary>
        /// Sets or gets the flag indicating whether the obfuscator should rename functions
        /// </summary>
        public bool ObfuscateFunctions
        {
            get { return _obfuscateFunctionNames; }
            set { _obfuscateFunctionNames = value; }
        }

        /// <summary>
        /// Sets or gets the flag indicating whether the obfuscator should remove whitespace. This includes comments. 
        /// </summary>
        public bool RemoveWhitespace
        {
            get { return _removeWhitespace; }
            set { _removeWhitespace = value; }
        }

        /// <summary>
        /// Gets or Sets the list of functions that will not be renamed
        /// </summary>
        public List<string> ExcludedFunctions
        {
            get
            {
                return _excludedFunctions;
            }
            set
            {
                _excludedFunctions = value;
            }
        }

        /// <summary>
        /// Gets or sets the list of variables that will not be renames. 
        /// Useful for setting variables defined in HTML, or variable to be included globally from
        /// externally, non-obfuscated files, such as config.php files. 
        /// </summary>
        public List<string> ExcludedVariables
        {
            get
            {
                return _excludedVariables;
            }
            set
            {
                _excludedVariables = value;
            }
        }

        /// <summary>
        /// Starts the obfuscation process based on a ObfuscationConfig file, generated with the 
        /// PHP Obfuscator GUI. 
        /// </summary>
        /// <param name="config">Config class generated by the PHP Obfuscator GUI</param>
        /// <param name="async">Flag indicating whether to run this process asynchronously. If true, this function will 
        /// return as soon as the file copy is done. False will result in the function returning only after all obfuscation
        /// has completed. </param>
        public void Start(ObfuscationConfig config, bool async)
        {
            _config = config;
            if (null == _config)
                return;

            this.ExcludedVariables = new List<string>(_config.ExcludeVariables);
            this.ExcludedFunctions = new List<string>(_config.ExcludeFunctions);
            this.RemoveWhitespace = _config.RemoveWhitespace;
            this.ObfuscateFunctions = _config.RenameFunctions;
            this.ObfuscateVariables = _config.RenameVariables;

            Start(_config.SourceDir, _config.TargetDir, new List<string>(_config.FilesToObfuscate), true);
        }

        /// <summary>
        /// Starts the obfuscation process. All parameters related to the obfuscation process must be set manually,
        /// and not through the use of a ObfuscationConfig file. 
        /// </summary>
        /// <param name="sourceDir">Source code directory</param>
        /// <param name="targetDir">Target directory for the obfuscated files</param>
        /// <param name="filesToObfuscate">Files from the source directory that are to be obfuscated</param>
        /// <param name="async">Flag indicating whether to run this process asynchronously. If true, this function will 
        /// return as soon as the file copy is done. False will result in the function returning only after all obfuscation
        /// has completed.</param>
        public void Start(string sourceDir, string targetDir, List<string> filesToObfuscate, bool async)
        {
            
            // if the target directory exists, wipe it out
            try
            {
                if (Directory.Exists(targetDir))
                {
                    Directory.Delete(targetDir, true);
                }
            }
            catch (Exception)
            {
                _ui.Error("Could not delete target directory: " + targetDir);
                return;
            }

            try
            {
                // if the target does not exist, create it. 
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }
            }
            catch (Exception)
            {
                _ui.Error("Could not create target directory: " + targetDir);
                return;
            }

            try
            {
                // copy every file recursively from the source to the target directory
                string[] files = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories);
                _targetFiles = new List<string>(files.Length);

                foreach (string file in files)
                {
                    string targetFile = file.Replace(sourceDir, targetDir);
                    if (filesToObfuscate.Contains(file))
                        _targetFiles.Add(targetFile);

                    FileInfo info = new FileInfo(targetFile);
                    if (!info.Directory.Exists)
                        info.Directory.Create();

                    _ui.StatusUpdate("Copying file: " + targetFile);
                    File.Copy(file, targetFile);

                }
            }
            catch (Exception)
            {
                _ui.Error("Could not copy files from source to target directory");
                return;
            }

            try
            {

                if (async)
                {
                    Thread obfThread = new Thread(new ThreadStart(ObfThread));
                    obfThread.Start();
                }
                else
                {
                    ObfThread();
                }
            }
            catch (Exception exp)
            {
                _ui.Error("There was an error obfuscating: " + exp.Message);
                return;
            }

        }

        /// <summary>
        /// Main thread of execution for obfuscation.
        /// </summary>
        private void ObfThread()
        {
            _functionsToReplace.Clear();
            _classesToReplace.Clear();

            // files are copied. Obfuscate those that were selected. 
            foreach (string file in _targetFiles)
            {
                _ui.StatusUpdate("Obfuscating " + file);

                try
                {
                    Obfuscate(file);
                }
                catch(Exception exp)
                {
                    _ui.Error(exp.Message);
                    return;
                }
            }

            if (_obfuscateFunctionNames)
            {
                // if the option is on, then the list of function names was constructed in the last pass.
                // go through the files again, and replace the function names. 
                foreach (string file in _targetFiles)
                {
                    try
                    {
                        _ui.StatusUpdate("Obfuscating Function Names in " + file);
                        RenameFunctions(file);
                    }
                    catch(Exception exp)
                    {
                        _ui.Error(exp.Message);
                        return;
                    }

                }

            }

            _ui.Done();
        }

        /// <summary>
        /// Determines if a character index of a string occurs within one of the regex matches from the 
        /// same string
        /// </summary>
        /// <param name="collection">Matches generated using Regex</param>
        /// <param name="stringIdx">Index of the character to check for inclusion in one of the matches</param>
        /// <returns>Flag indicating whether the index was in one of the ranges. </returns>
        private bool InMatchedCollection(MatchCollection collection, int stringIdx)
        {
            foreach (Match match in collection)
            {
                if(match.Index < stringIdx && ((match.Index + match.Length) > stringIdx))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Determine the index of a string within a larger string. Strings embdedded in the haystack are not
        /// checked for the target needle... that is to say quoted strings appearing in the haystack as they 
        /// would in source code are ignored. if searching for the string TEST, it will be found only if it does
        /// not appear within quotes.
        /// </summary>
        /// <param name="needle">String for which we are searching</param>
        /// <param name="haystack">String being searched</param>
        /// <param name="start">starting position at which to start searching the haystack</param>
        /// <param name="exclusionaryStrings">whether or not to consider the starting position when looking for strings to avoid</param>
        /// <returns></returns>
        private int IndexOf(string needle, string haystack, int start, bool exclusionaryStrings)
        {
            // find the strings so we can ignore their contents. 
            Regex stringPattern = new Regex((string)Properties.Settings.Default["stringPattern"]);
            MatchCollection avoid = null;
            if(exclusionaryStrings)
                avoid = stringPattern.Matches(haystack, start);
            else
                avoid = stringPattern.Matches(haystack);
            
            int found = haystack.IndexOf(needle, start);

            // if it didnt find anything, return
            if (found < 0)
                return found;

            // if it found something and it happens to be in a range we should avoid, recurse from that position
            if (InMatchedCollection(avoid, found))
                return IndexOf(needle, haystack, found + 1, exclusionaryStrings);

            return found;
        }

        /// <summary>
        /// Obfuscates the variable names in a block of code and collects function names for use in the second pass of obfuscation
        /// </summary>
        /// <param name="codeBlock">Block of PHP code to obfuscate</param>
        /// <returns>Obfuscated block of code. </returns>
        private string ObfuscateBlock(string codeBlock)
        {
            // first remove comments in the form /* */ that are not within strings
            int start = 0;
            while (start >= 0)
            {
                start = IndexOf("/*", codeBlock, 0, false);
                //start = codeBlock.IndexOf("/*");
                if (start >= 0)
                {
                    int end = IndexOf("/*", codeBlock, start, true);
                    //int end = codeBlock.IndexOf("*/", start);
                    if (end >= 0)
                        codeBlock = codeBlock.Remove(start, end - start + 2);
                }
            }

            // remove other forms of comments
            if (_removeWhitespace)
            {
                codeBlock = RemoveComments("//", codeBlock);
                codeBlock = RemoveComments("#", codeBlock);
            }

            // rename variables
            if (_obfuscateVariables)
            {
                codeBlock = RenameVariables(codeBlock);
            }

            // remove newlines
            if (_removeWhitespace)
            {
                Regex whitespacePattern = new Regex("([\t\n\r])");
                codeBlock = whitespacePattern.Replace(codeBlock, " ");
            }

            if (_obfuscateFunctionNames)
            {
                Regex classPattern = new Regex((string)Properties.Settings.Default["classDeclarationPattern"]);
                MatchCollection collection = classPattern.Matches(codeBlock);

                foreach (Match match in collection)
                    _classesToReplace.Add(match.Value);
                
                Regex functionPattern = new Regex((string)Properties.Settings.Default["functionDeclarationPattern"]);
                collection = functionPattern.Matches(codeBlock);

                foreach (Match match in collection)
                {
                    // dont add class constructors to the function list
                    if(!_classesToReplace.Contains(match.Value) && !_excludedFunctions.Contains(match.Value) && !phpFunctions.Contains(match.Value))
                        _functionsToReplace.Add(match.Value);
                }


            }

            return codeBlock;

        }

        /// <summary>
        /// Returns an MD5 representation of a string
        /// </summary>
        /// <param name="originalString"></param>
        /// <returns></returns>
        private string GetMD5(string originalString)
        {
            Byte[] originalBytes;
            Byte[] encodedBytes;

            originalBytes = ASCIIEncoding.Default.GetBytes(originalString);
            encodedBytes = _md5.ComputeHash(originalBytes);

            return BitConverter.ToString(encodedBytes).Replace("-", "");
        }

        /// <summary>
        /// Renames all the variables in a block of PHP code to their MD5 modified equivalents. 
        /// </summary>
        /// <param name="codeBlock"></param>
        /// <returns></returns>
        private string RenameVariables(string codeBlock)
        {
            Regex variablePattern = new Regex((string)Properties.Settings.Default["variablePattern"]);
            MatchCollection collection = variablePattern.Matches(codeBlock);

            // replace the matches backwards so we dont need to keep track of modification offsets. 
            for (int idx = collection.Count - 1; idx >= 0; idx--)
            {
                Match match = collection[idx];
                if (!_excludedVariables.Contains(match.Value))
                {
                    string encodedVar = "$R" + GetMD5(match.Value);
                    codeBlock = codeBlock.Remove(match.Index, match.Length);
                    codeBlock = codeBlock.Insert(match.Index, encodedVar);
                }
            }

            return codeBlock;
        }

        /// <summary>
        /// Removes a type of single line comments from a block of code
        /// </summary>
        /// <param name="form">Form of the Commen. Can be // or #, or anything else determined to signify a comment</param>
        /// <param name="code">Block of code from which to remove comments</param>
        /// <returns>Obfuscated block of code</returns>
        private string RemoveComments(string form, string code)
        {
            int start = 0;
            while (start >= 0)
            {
                start = IndexOf(form, code, start, false);
                if (start >= 0)
                {
                    int len = 0;
                    int end = code.IndexOf("?>", start);
                    if (end >= 0)
                        len = 2;

                    if (end < 0)
                    {
                        end = code.IndexOf("\n", start);
                        len = 1;
                    }

                    if (end < 0)
                        end = code.Length - 1;

                    if (end >= 0)
                    {
                        code = code.Remove(start, end - start - (len - 1));
                    }
                }
            }

            return code;
        }

        /// <summary>
        /// Renames function declarations and function calls in the specified file based on the function
        /// names gathered in a previous obfuscation pass. 
        /// </summary>
        /// <param name="filename">Filename to obfuscate</param>
        private void RenameFunctions(string filename)
        {
            // if the file does not end in ".php", return 
            FileInfo info = new FileInfo(filename);

            if (info.Extension.ToLower() != ".php")
                return;

            FileStream stream = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
            StreamReader reader = new StreamReader(stream);

            string fileContents = reader.ReadToEnd();
            reader.Close();
            stream.Close();

            Regex functionPattern = new Regex((string)Properties.Settings.Default["functionPattern"]);
            Regex classPattern = new Regex((string)Properties.Settings.Default["classDeclarationPattern"]);

            int start = 0;
            while (start >= 0)
            {
                int blockSize;
                string codeBlock = GetCodeBlock(fileContents, start, out start, out blockSize);

                if (start >= 0)
                {
                    MatchCollection collection = functionPattern.Matches(codeBlock);
                    for (int idx = collection.Count - 1; idx >= 0; idx--)
                    {
                        Match match = collection[idx];
                        int replacementFunctionIdx = _functionsToReplace.IndexOf(match.Value);

                        if (replacementFunctionIdx >= 0)
                        {
                            codeBlock = codeBlock.Remove(match.Index, match.Length);
                            codeBlock = codeBlock.Insert(match.Index, "F" + GetMD5(match.Value));
                        }
                        else
                        {
                            // if it isnt function use, it may be class construction
                            replacementFunctionIdx = _classesToReplace.IndexOf(match.Value);
                            if (replacementFunctionIdx >= 0)
                            {
                                codeBlock = codeBlock.Remove(match.Index, match.Length);
                                codeBlock = codeBlock.Insert(match.Index, "C" + GetMD5(match.Value));
                            }
                        }
                    }

                    // match class declarations
                    collection = classPattern.Matches(codeBlock);
                    for (int idx = collection.Count - 1; idx >= 0; idx--)
                    {
                        Match match = collection[idx];
                        int replacementClassIdx = _classesToReplace.IndexOf(match.Value);

                        if (replacementClassIdx >= 0)
                        {
                            codeBlock = codeBlock.Remove(match.Index, match.Length);
                            codeBlock = codeBlock.Insert(match.Index, "C" + GetMD5(match.Value));
                        }
                    }

                    fileContents = fileContents.Remove(start, blockSize);
                    fileContents = fileContents.Insert(start, codeBlock);

                    start = start + codeBlock.Length;
                }
            }

            stream = new FileStream(filename, FileMode.Create);
            StreamWriter writer = new StreamWriter(stream);
            writer.Write(fileContents);
            writer.Close();
            stream.Close();

        }

        /// <summary>
        /// Gets the next block of code
        /// </summary>
        /// <param name="fileContents">Complete contents of a PHP file</param>
        /// <param name="start">Starting position from which to search for a block of code</param>
        /// <param name="blockStart">Starting position of the next found block of code</param>
        /// <param name="blockSize">Size of the block of code that was located</param>
        /// <returns></returns>
        private string GetCodeBlock(string fileContents, int start, out int blockStart, out int blockSize)
        {
            //find each php block
            int first = fileContents.IndexOf("<?", start);
            int first2 = fileContents.IndexOf("<?php", start);

            int len = 2;

            if (first2 >= 0 && first2 < first)
            {
                first = first2;
                len = 5;
            }

            start = first + len;

            if (first >= 0)
            {
                int end = IndexOf("?>", fileContents, first, true);
                if (end >= 0)
                {
                    blockStart = start;
                    blockSize = end - start;

                    string codeBlock = fileContents.Substring(blockStart, blockSize);
                    return codeBlock;

                }

            }


            blockStart = -1;
            blockSize = -1;
            return fileContents;
        }

        /// <summary>
        /// Start obfuscation on a PHP file. 
        /// </summary>
        /// <param name="filename">PHP file to obfuscate</param>
        private void Obfuscate(string filename)
        {
            // if the file does not end in ".php", return 
            FileInfo info = new FileInfo(filename);
            
            if (info.Extension.ToLower() != ".php")
                return;

            // if the file is read only, change the attribute. 
            if (FileAttributes.ReadOnly == (info.Attributes & FileAttributes.ReadOnly))
            {
                File.SetAttributes(filename, info.Attributes & ~FileAttributes.ReadOnly);
            }

            FileStream stream = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
            StreamReader reader = new StreamReader(stream);
            
            string fileContents = reader.ReadToEnd();
            Encoding encoding = reader.CurrentEncoding;

            reader.Close();
            stream.Close();

            int blockStart = 0;
            while (blockStart >= 0)
            {
                int blockSize;
                string codeBlock = GetCodeBlock(fileContents, blockStart, out blockStart, out blockSize);
                if (blockStart >= 0 && blockSize > 0)
                {
                    codeBlock = ObfuscateBlock(codeBlock);
                    fileContents = fileContents.Remove(blockStart, blockSize);
                    fileContents = fileContents.Insert(blockStart, codeBlock);
                    blockStart = blockStart + codeBlock.Length;
                }
            }

            stream = new FileStream(filename, FileMode.Create);
            StreamWriter writer = new StreamWriter(stream, encoding);
            writer.Write("<? /* This file encoded by Raizlabs PHP Obfuscator http://www.raizlabs.com/software */ ?>\n");
            writer.Write(fileContents);
            writer.Close();
            stream.Close();
            
        }

        /// <summary>
        /// Dump the regular expressions being utilized for pattern matching to the UI. 
        /// </summary>
        public void OutputPatterns()
        {
            _ui.StatusUpdate("Using pattern for function declarations: " + Properties.Settings.Default["functionDeclarationPattern"]);
            _ui.StatusUpdate("Using pattern for function calls: " + Properties.Settings.Default["functionPattern"]);
            _ui.StatusUpdate("Using pattern for class declarations calls: " + Properties.Settings.Default["classDeclarationPattern"]);
            _ui.StatusUpdate("Using pattern for variables: " + Properties.Settings.Default["variablePattern"]);
            _ui.StatusUpdate("Using pattern for strings: " + Properties.Settings.Default["stringPattern"]);
        }
    }
}
