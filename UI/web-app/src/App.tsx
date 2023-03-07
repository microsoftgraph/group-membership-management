// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
import { useState, useEffect } from 'react';
import './App.css';
import Loader from './components/Loader';
import Job from './components/Job';

function App() {
  const [jobs, setJobs] = useState([]);
  const [err, setError] = useState({});

  useEffect(() => {
    fetch('https://demo6035515.mockable.io/groups')
    .then(response => response.json())
    .then(res => setJobs(res))
    .catch(err => setError(err))
  }, [])

  return (
    <div className="App">
      {jobs.length > 0 ? <Job jobs={jobs} /> : (<Loader />)}
    </div>
  );
}

export default App;