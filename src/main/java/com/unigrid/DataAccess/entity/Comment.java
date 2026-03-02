package com.unigrid.DataAccess.entity;

import jakarta.persistence.*;
import lombok.Getter;
import lombok.Setter;
import org.hibernate.annotations.ColumnDefault;
import org.hibernate.annotations.Nationalized;

import java.time.Instant;
import java.util.UUID;

@Getter
@Setter
@Entity
@Table(name = "Comment", schema = "dbo")
public class Comment {
    @Id
    @ColumnDefault("newsequentialid()")
    @Column(name = "commentId", nullable = false)
    private UUID id;

    @Column(name = "taskId", nullable = false)
    private UUID taskId;

    @Column(name = "memberId", nullable = false)
    private UUID memberId;

    @Nationalized
    @Lob
    @Column(name = "content", nullable = false)
    private String content;

    @ColumnDefault("sysdatetime()")
    @Column(name = "createdAt")
    private Instant createdAt;

    @Column(name = "updatedAt")
    private Instant updatedAt;


}