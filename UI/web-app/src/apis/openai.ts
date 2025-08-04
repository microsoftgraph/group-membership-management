import axios from 'axios';

const openaiApiKey = process.env.REACT_APP_OPENAI_API_KEY;
const openaiApiEndpoint = process.env.REACT_APP_OPENAI_API_ENDPOINT;

const openaiApiUrl = `${openaiApiEndpoint}/openai/deployments/gpt-4o-mini/chat/completions?api-version=2025-01-01-preview`

interface OpenAIResponse {
  choices: { message: { content: string } }[];
}

const sleep = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));

const fetchWithRetry = async (prompt: string, retries: number = 3, backoff: number = 3000): Promise<any> => {
  try {
    return await axios.post<OpenAIResponse>(openaiApiUrl,
      {
        messages: [{ role: 'user', content: prompt }],
        max_tokens: 60,
        temperature: 0.7
      },
      {
        headers: {
          'Content-Type': 'application/json',
          'api-key': openaiApiKey
        }
      });
  } catch (error: any) {
    if (error.response && error.response.status === 429 && retries > 0) {
      console.warn(`Rate limit exceeded. Retrying in ${backoff}ms...`);
      await sleep(backoff);
      return fetchWithRetry(prompt, retries - 1, backoff * 2);
    } else {
      throw error;
    }
  }
};

export const generateTitleUsingOpenAI = async (filter?: string): Promise<string> => {
  return "test";
  // console.log("filter", filter, filter?.length);
  // let title = "";
  // let prompt = '';

  // const createPrompt = (filter: string) => {

  //   return `

  //   ---INSTRUCTIONS---

  //   Create a title based on the following ${filter} filter.

  //   ${filter} is a SQL WHERE clause.

  //   This is the standard filter format:

  //   [ATTRIBUTE] [OPERATOR] [VALUE] [AND/OR] [ATTRIBUTE] [OPERATOR] [VALUE] [AND/OR] [ATTRIBUTE] [OPERATOR] [VALUE] ...

  //   For example: CountryName = 'USA' AND ChildCount > 2

  //   ATTRIBUTE is the Column Name in SQL table

  //   OPERATOR can be one of the following: =, <>, >, <, >=, <=

  //   VALUE is the value of the Column

  //   AND/OR is the logical operator to combine multiple conditions. This can be either AND or OR. If there is only one condition, there is no AND/OR.

  //   Create a meaningful title that summarizes the filter conditions. For example, if the filter includes 'CountryName = 'USA' AND ChildCount > 2', the title could be 'All users in USA with more than 2 children'.

  //   If the filter is too complex to summarize in a single title, you can provide a partial title that summarizes part of the filter conditions.

  //   Don't include any prefixes such as "Title:".

  //   `;
  // };

  // if (filter) {
  //   prompt = createPrompt(filter);
  // }

  // try {
  //   const response = await fetchWithRetry(prompt);
  //   console.log("response", response);
  //   let generatedTitle = response.data.choices[0].message.content.trim();
  //   console.log("generatedTitle", generatedTitle);
  //   if (
  //     generatedTitle.includes("Hello! How can I assist you today?") ||
  //     generatedTitle.includes("group name is missing") ||
  //     generatedTitle.includes("group name is blank") ||
  //     generatedTitle.includes("group name is empty") ||
  //     generatedTitle.includes("All users in ''")
  //   ) {
  //     generatedTitle = '';
  //   }
  //   title = generatedTitle.replace(/["]+/g, '');
  // } catch (error) {
  //   console.error('Error calling OpenAI API:', error);
  //   title = 'Error generating title';
  // }
  // console.log("title", title);
  // return title;
};