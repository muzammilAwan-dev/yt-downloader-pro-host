private void HandleOutput(string? data, DownloadTask task)
        {
            if (string.IsNullOrWhiteSpace(data)) return;

            lock (_bufferLock)
            {
                _recentOutput.AppendLine(data);
                if (_recentOutput.Length > 10000)
                {
                    var text = _recentOutput.ToString();
                    _recentOutput.Clear();
                    _recentOutput.Append(text.Substring(text.Length - 5000));
                }
            }

            // Perform Regex calculations safely on the background thread
            bool needsUiUpdate = false;
            DownloadStatus newStatus = task.Status;
            double newProgress = task.Progress;
            string newSpeed = task.Speed;
            string newEta = task.Eta;
            string newOutputPath = task.OutputPath;
            string newTitle = task.Title;

            if (newStatus != DownloadStatus.Downloading)
            {
                newStatus = DownloadStatus.Downloading;
                needsUiUpdate = true;
            }

            if (data.Contains("[download]"))
            {
                var percentMatch = Regex.Match(data, @"\[download\]\s+(?:(\d+\.?\d*)%|100%)");
                if (percentMatch.Success)
                {
                    newProgress = double.TryParse(percentMatch.Groups[1].Value, out var percent) ? Math.Min(percent, 100.0) : 100.0;
                    needsUiUpdate = true;
                }

                var speedMatch = Regex.Match(data, @"at\s+([\d\.]+[KMG]iB/s)");
                if (speedMatch.Success) { newSpeed = speedMatch.Groups[1].Value; needsUiUpdate = true; }

                var etaMatch = Regex.Match(data, @"ETA\s+([\d:]+)");
                if (etaMatch.Success) { newEta = etaMatch.Groups[1].Value; needsUiUpdate = true; }
            }

            if (data.Contains("Destination:", StringComparison.OrdinalIgnoreCase))
            {
                var destMatch = Regex.Match(data, @"Destination:\s+(.+)");
                if (destMatch.Success)
                {
                    newOutputPath = destMatch.Groups[1].Value.Trim();
                    string tempFileName = Path.GetFileName(newOutputPath);
                    if (!string.IsNullOrEmpty(tempFileName) && newTitle == "Unknown") newTitle = tempFileName;
                    OnInfoExtracted?.Invoke(this, new ExtractedInfoEventArgs(task.Id, newTitle, newOutputPath));
                    needsUiUpdate = true;
                }
            }

            // THE FIX: Push ALL calculated UI updates safely to the main Dispatcher thread at once
            if (needsUiUpdate)
            {
                var now = DateTime.Now;
                if ((now - _lastUpdate).TotalMilliseconds >= 100 || newProgress >= 100.0)
                {
                    _lastUpdate = now;
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        task.Status = newStatus;
                        task.Progress = newProgress;
                        task.Speed = newSpeed;
                        task.Eta = newEta;
                        task.OutputPath = newOutputPath;
                        task.Title = newTitle;
                    });
                    
                    OnProgressUpdate?.Invoke(this, new ProgressEventArgs(task.Id, newProgress, newSpeed, newEta));
                }
            }
        }
