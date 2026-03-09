import {defineStore} from 'pinia';
import {computed, ref} from 'vue';
import {chatService} from '@/services/chatService.ts';
import {
  type ChatChunk,
  ChunkType, type FunctionApprovalRequest,
  type IntentResult,
  MessageRole,
  type Session, type Widget
} from "@/types/protocols";
import type {
  ApprovalChunk,
  ChatMessage,
  FunctionCall,
  FunctionCallChunk, IntentChunk,
  WidgetChunk
} from "@/types/models.ts";

export const useChatStore = defineStore('chat', () => {
  // ================= 状态 (State) =================

  const sessions = ref<Session[]>([]);
  const currentSessionId = ref<string | null>(null);
  const messagesMap = ref<Record<string, ChatMessage[]>>({});
  const isStreaming = ref(false);
  const isWaitingForApproval = ref(false);

  // ================= 计算属性 =================

  const currentMessages = computed(() => {
    if (!currentSessionId.value) return [];
    return messagesMap.value[currentSessionId.value] || [];
  });

  const currentSession = computed(() => {
    if (!currentSessionId.value) return { title: '当前没有选择会话' } as Session;
    return sessions.value.find(session => session.id === currentSessionId.value)
  });

  // ================= 动作 (Actions) =================

  async function init() {
    try {
      sessions.value = await chatService.getSessions();
    } catch (error) {
      console.error('无法加载会话', error);
    }
  }

  async function createNewSession() {
    const newSession = await chatService.createSession();
    sessions.value.unshift(newSession);
    currentSessionId.value = newSession.id;
    messagesMap.value[newSession.id] = [];
    isStreaming.value = false;
    isWaitingForApproval.value = false;
  }

  async function selectSession(id: string) {
    currentSessionId.value = id;
    isWaitingForApproval.value = false;
  }

  async function sendMessage(input: string) {
    if (!currentSessionId.value || isStreaming.value) return;

    const sessionId = currentSessionId.value;

    const userMsg: ChatMessage = {
      sessionId,
      role: MessageRole.User,
      chunks : [{
        source: 'User',
        type: ChunkType.Text,
        content: input
      }],
      isStreaming: false,
      timestamp: Date.now()
    };
    addMessage(sessionId, userMsg);

    const aiMsg: ChatMessage = {
      sessionId,
      role: MessageRole.Assistant,
      chunks: [],
      isStreaming: true,
      timestamp: Date.now()
    };
    const targetMsg = addMessage(sessionId, aiMsg);

    isStreaming.value = true;

    await chatService.sendMessageStream(sessionId, input, {
      onChunkReceived: (chunk: ChatChunk) => {
        processChunk(targetMsg!, chunk);
      },
      onComplete: () => {
        isStreaming.value = false;
        targetMsg.isStreaming = false;
      },
      onError: (err) => {
        isStreaming.value = false;
      }
    });
  }

  async function submitApproval(callId: string, chunk: ApprovalChunk) {
    if (!currentSessionId.value) return;
    const sessionId = currentSessionId.value;

    try {
      isStreaming.value = true;
      let targetMsg = getLastAssistantMessage(sessionId);
      if (!targetMsg) {
        targetMsg = addMessage(sessionId, {
          sessionId,
          role: MessageRole.Assistant,
          chunks: [],
          isStreaming: true,
          timestamp: Date.now()
        });
      }

      const messageText = chunk.status === 'approved' ? "批准" : "拒绝";
      await chatService.sendMessageStream(
        sessionId,
        messageText,
        {
          onChunkReceived: (chunk: ChatChunk) => {
            processChunk(targetMsg!, chunk);
          },
          onComplete: () => {
            isStreaming.value = false;
            if (targetMsg) targetMsg.isStreaming = false;
            isWaitingForApproval.value = false;
          },
          onError: (err) => {
            console.error('审批响应流中断:', err);
            isStreaming.value = false;
            isWaitingForApproval.value = false;
          }
        },
        [callId]
      );

    } catch (error) {
      console.error('提交审批失败:', error);
      isStreaming.value = false;
    }
  }

  // ================= 辅助函数 (Internal) =================

  function addMessage(sid: string, msg: ChatMessage): ChatMessage {
    if (!messagesMap.value[sid]) {
      messagesMap.value[sid] = [];
    }
    const list = messagesMap.value[sid]!;
    list.push(msg);
    return list[list.length - 1]!;
  }

  function addTextChunk(msg: ChatMessage, chunk: ChatChunk) {
    const preChunk = msg.chunks[msg.chunks.length - 1];
    if (preChunk === undefined) {
      msg.chunks.push(chunk);
      return;
    }
    if (preChunk.source === chunk.source && preChunk.type === ChunkType.Text) {
      preChunk.content += chunk.content;
    } else {
      msg.chunks.push(chunk);
    }
  }

  function addWidgetChunk(msg: ChatMessage, chunk: ChatChunk, parsedWidget: any) {
    const widgetChunk = {
      ...chunk,
      type: ChunkType.Widget,
      widget: parsedWidget
    } as unknown as WidgetChunk;
    msg.chunks.push(widgetChunk);
  }

  function addIntentChunk(msg: ChatMessage, chunk: ChatChunk) {
    try {
        const intents = JSON.parse(chunk.content) as IntentResult[];
        const intentChunk = { ...chunk, intents } as IntentChunk;
        msg.chunks.push(intentChunk);
    } catch (e) { addTextChunk(msg, chunk); }
  }

  function addFunctionCallChunk(msg: ChatMessage, chunk: ChatChunk) {
    try {
        const functionCall = JSON.parse(chunk.content) as FunctionCall;
        functionCall.status = 'calling';
        const fcChunk = { ...chunk, functionCall } as FunctionCallChunk;
        msg.chunks.push(fcChunk);
    } catch (e) { addTextChunk(msg, chunk); }
  }

  function addFunctionResultChunk(msg: ChatMessage, chunk: ChatChunk) {
    try {
        const functionResult = JSON.parse(chunk.content) as FunctionCall;
        const functionCallChunks = msg.chunks
          .filter(c => c.type === ChunkType.FunctionCall) as FunctionCallChunk[];
        const fcChunk = functionCallChunks.find(c => c.functionCall.id === functionResult.id);
        if (fcChunk) {
          fcChunk.functionCall.result = functionResult.result;
          fcChunk.functionCall.status = 'completed';
        }
    } catch (e) {}
  }

  function addApprovalRequestChunk(msg: ChatMessage, chunk: ChatChunk) {
    try {
      const requestPayload = JSON.parse(chunk.content) as FunctionApprovalRequest;
      const approvalChunk: ApprovalChunk = {
        ...chunk,
        request: requestPayload,
        status: 'pending'
      };
      msg.chunks.push(approvalChunk);
      isWaitingForApproval.value = true;
    } catch (error) {
      console.error('解析审批请求失败:', error);
    }
  }

  function getLastAssistantMessage(sid: string): ChatMessage | null {
    const list = messagesMap.value[sid];
    if (!list || list.length === 0) return null;
    const lastMsg = list[list.length - 1]!;
    if (lastMsg.role === MessageRole.Assistant) {
      return lastMsg;
    }
    return null;
  }

  function processChunk(msg: ChatMessage, chunk: ChatChunk) {
    // 拦截 Text 中的 visual_decision
    if (chunk.type === ChunkType.Text) {
      const content = chunk.content.trim();
      if (content.includes('"visual_decision"') || content.includes('"VisualDecision"')) {
        try {
          const jsonMatch = content.match(/(\{[\s\S]*"visual_decision"[\s\S]*?\})/);
          if (jsonMatch) {
            const jsonStr = jsonMatch[0];
            const payload = JSON.parse(jsonStr);
            const decision = payload.visual_decision || payload.VisualDecision;
            
            if (decision) {
               // 【核心逻辑】：去前面的 FunctionResult 找数据
               let finalData: any[] = [];
               
               // 1. 优先从 JSON 自身找
               if (payload.data && Array.isArray(payload.data) && payload.data.length > 0) {
                   finalData = payload.data;
               } else if (decision.data && Array.isArray(decision.data) && decision.data.length > 0) {
                   finalData = decision.data;
               } 
               else {
                   // 2. 从消息历史中查找最近的 FunctionResult
                   // 【修复点】：增加 !c 的判断，防止 TS 报错
                   for (let i = msg.chunks.length - 1; i >= 0; i--) {
                       const c = msg.chunks[i];
                       if (!c) continue; // 关键修复：如果 c 为 undefined 则跳过

                       if (c.type === ChunkType.FunctionCall) {
                           const fc = (c as FunctionCallChunk).functionCall;
                           // 确保 status 和 result 都存在
                           if (fc && fc.status === 'completed' && fc.result) {
                               try {
                                   const parsed = JSON.parse(fc.result);
                                   if (Array.isArray(parsed) && parsed.length > 0) {
                                       finalData = parsed;
                                       break; // 找到了就停止
                                   }
                               } catch (e) {}
                           }
                       }
                   }
               }

               // 注入数据
               const widgetPayload = { ...payload, data: finalData };
               addWidgetChunk(msg, chunk, widgetPayload);
               
               const remainingText = content.replace(jsonStr, '').trim();
               if (remainingText) addTextChunk(msg, { ...chunk, content: remainingText });
               return;
            }
          }
        } catch (e) { }
      }
    }

    switch (chunk.type) {
      case ChunkType.Text: addTextChunk(msg, chunk); break;
      case ChunkType.Intent: addIntentChunk(msg, chunk); break;
      case ChunkType.FunctionCall: addFunctionCallChunk(msg, chunk); break;
      case ChunkType.FunctionResult: addFunctionResultChunk(msg, chunk); break;
      case ChunkType.Widget: 
        try { addWidgetChunk(msg, chunk, JSON.parse(chunk.content)); } 
        catch (e) { addTextChunk(msg, chunk); }
        break;
      case ChunkType.ApprovalRequest: addApprovalRequestChunk(msg, chunk); break;
    }
  }

  return { sessions, currentSessionId, currentSession, currentMessages, isStreaming, isWaitingForApproval, init, createNewSession, selectSession, sendMessage, submitApproval };
});