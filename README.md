# GitHub Rate Limit Checker  
  
## Purpose  
  
The primary goal of this project is to provide GitHub Enterprise administrators with a comprehensive overview of API usage across all applications within the organization. By monitoring and logging API requests, this tool helps identify potential unnecessary exhaustion of API limits.  
  
### Key Benefits:  
  
- **Enhanced Visibility**: Gain insights into how different applications are utilizing the GitHub API.  
- **Proactive Management**: Identify and address applications that are excessively using API resources, potentially preventing disruptions.  
- **Resource Optimization**: Ensure that API limits are used efficiently, maintaining availability and performance for all users and applications.  
- **Data-Driven Decisions**: Use logged data to make informed decisions about application management and resource allocation.  
  
If an admin finds an app that is excessively using the APIs and may not be needed, they can address it internally. This tool ensures that the GitHub API remains available and efficient for all users and their applications.  
  
## Features  
  
- **Authorization**: Uses OAuth to authorize each application.  
- **API Request Monitoring**: Makes multiple API requests to track usage.  
- **Rate Limit Logging**: Logs rate limit data for each application and saves it to a JSON file for review.  
  
## Usage  
  
1. **Authorization**:  
    - The application will provide a URL for each app.  
    - Visit the URL in your browser to authorize the app.  
    - Close the window once authorization is successful.  
  
2. **API Requests and Rate Limit Logging**:  
    - The application will make API requests and fetch rate limit data for each app.  
    - Logs will be saved to `rate_limit_logs.json`.  
  
Happy monitoring! If you have any questions or need further assistance, feel free to submit issues or pull requests.  
