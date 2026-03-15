# Goal

Build a desktop application to write some plain text and HTML content in tabular data format to the clipboard in a delayed manner. The application will promise the "text/plain" and "text/html" formats to the clipboard, but will only provide or write the actual content when requested by the clipboard viewer or application that reads these formats.

# Here are the requirements for the desktop application:

1. It should target Windows 11 only. No MAC or Linux support is needed.
2. It should be as simple as possible.
3. Choose a programming language and framework that are well-suited for Windows desktop application development. But I am familiar with C# and .NET, let me know if you think there is a better choice.

# Application Features:

    1. The application should have a simple user interface with two input fields: one for the number of rows and another for the number of columns in the table. And there should be a "Copy to Clipboard" button to trigger the clipboard writing process.
    2. There should also be two checkboxes to allow the user to select whether they want to include plain text, HTML content, or both in the clipboard data. The application should generate the corresponding content based on the user's selection when the "Copy to Clipboard" button is clicked.
    3. When the user clicks the "Copy to Clipboard" button, the application should just promise the "text/plain", "text/html" formats to the clipboard based on the user's selection, but it should not generate or write the actual content to the clipboard at this point. Instead, the application should wait until the clipboard viewer or application requests the content for these formats. When the content is requested, the application should generate the content on-the-fly based on the number of rows and columns specified by the user and write it to the clipboard in the requested format.
    4. The plain text content should be generated in a simple tabular format, where each cell is separated by a tab character and each row is separated by a newline character. For example, if the user specifies 3 rows and 4 columns, the plain text content should look like this:
      ```
      Cell(1,1)    Cell(1,2)    Cell(1,3)    Cell(1,4)
      Cell(2,1)    Cell(2,2)    Cell(2,3)    Cell(2,4)
      Cell(3,1)    Cell(3,2)    Cell(3,3)    Cell(3,4)
      ```
    5. The HTML content should be generated in a simple table format, where each cell is represented by a `<td>` element and each row is represented by a `<tr>` element. For example, if the user specifies 3 rows and 4 columns, the HTML content should look like this:
      ```html
      <table>
        <tr>
          <td>Cell(1,1)</td>
          <td>Cell(1,2)</td>
          <td>Cell(1,3)</td>
          <td>Cell(1,4)</td>
        </tr>
        <tr>
          <td>Cell(2,1)</td>
          <td>Cell(2,2)</td>
          <td>Cell(2,3)</td>
          <td>Cell(2,4)</td>
        </tr>
        <tr>
          <td>Cell(3,1)</td>
          <td>Cell(3,2)</td>
          <td>Cell(3,3)</td>
          <td>Cell(3,4)</td>
        </tr>
      </table>
      ```
      6. The application should also include some delay mechanism to simulate a time-consuming content generation process. For example, you can introduce a delay of 10 seconds before writing the generated content to the clipboard when it is requested.

# Additional Notes:

1. Create a README file with instructions on how to build and run the application, as well as any dependencies that need to be installed.
2. Create a claude instruction file (if required) with detailed steps on how to implement the application, including code snippets and explanations for each step. This will help you stay organized and ensure that you cover all the necessary aspects of the application development process.
3. Create and update a memory bank or knowledge base with any relevant information, code snippets, or resources that you come across during the development process. This will help you keep track of your progress and make it easier to refer back to important information when needed by you or other developers in the future.
4. Use extensive comments in your code to explain the logic and flow of the application.
5. If you have any questions or need clarification on any aspect of the application development process, feel free to ask for help or guidance. It's important to ensure that you have a clear understanding of the requirements and the implementation steps before proceeding with the development.
6. First plan out the application architecture and design, including the user interface layout, the clipboard handling mechanism, and the content generation logic. This will help you have a clear roadmap for the development process and ensure that you cover all the necessary components of the application.
